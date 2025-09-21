using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Renci.SshNet;
using System.Web.Script.Serialization;

namespace Updater_2
{
    internal class Shh_UpdateFactor
    {
        const int MaxAttempts = 5;
        const int DelayMs = 500;

        static async Task<bool> UploadFileViaSftp(string ipAddress, string localFilePath, string remotePath, int rowIndex, string fileName)
        {
            try
            {
                using (var sftp = new SftpClient(ipAddress, UI.ssh_port, UI.ssh_username, UI.ssh_password))
                {
                    sftp.Connect();

                    // Ensure remote directory exists
                    var remoteDir = Path.GetDirectoryName(remotePath);
                    if (!sftp.Exists(remoteDir))
                    {
                        sftp.CreateDirectory(remoteDir);
                    }

                    // Upload file with progress tracking
                    using (var fileStream = new FileStream(localFilePath, FileMode.Open))
                    {
                        var fileInfo = new FileInfo(localFilePath);
                        long fileSize = fileInfo.Length;
                        long transferred = 0;

                        var asyncResult = sftp.BeginUploadFile(fileStream, remotePath);

                        // Progress monitoring
                        while (!asyncResult.IsCompleted)
                        {
                            await Task.Delay(100);
                            transferred = fileStream.Position;
                            double progress = (double)transferred / fileSize * 100;
                            UI.StatusDataGridView(rowIndex, fileName, $"Uploading... {progress:F1}%", Color.Yellow);
                        }

                        sftp.EndUploadFile(asyncResult);
                    }

                    sftp.Disconnect();
                    return true;
                }
            }
            catch (Exception ex)
            {
                UI.StatusDataGridView(rowIndex, fileName, $"Uploading Error: {ex.Message}", Color.Red);
                return false;
            }
        }

        static string GetStateAsync(string ipAddress)
        {
            try
            {
                using (var ssh = new SshClient(ipAddress, UI.ssh_port, UI.ssh_username, UI.ssh_password))
                {
                    ssh.Connect();
                    var command = ssh.RunCommand($"curl -s http://127.0.0.1/updater/state");
                    ssh.Disconnect();

                    // Simple JSON parsing to extract the "stage" value
                    var response = command.Result;
                    return new JavaScriptSerializer().Deserialize<dynamic>(response)["stage"];

                }
            }
            catch
            {
                return "undefined";
            }
        }

        static void SendCancelCommand(string ipAddress, int rowIndex, string fileName)
        {
            try
            {
                using (var ssh = new SshClient(ipAddress, UI.ssh_port, UI.ssh_username, UI.ssh_password))
                {
                    ssh.Connect();
                    var command = ssh.RunCommand(
                        "curl -X POST -H \"accept: text/plain\" " +
                        "-H \"Content-Type: multipart/form-data\" " +
                        "-F \"dummy= \" \"http://127.0.0.1/updater/cancel\""
                    );
                    ssh.Disconnect();
                }
            }
            catch
            {
            }
        }

        static async Task<bool> ExecuteWebInstall(string ipAddress, string remoteTempPath, int rowIndex, string fileName)
        {
            string state = "undefined";
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                state = GetStateAsync(ipAddress);
                if (state == "notStarted") break;
                await Task.Delay(DelayMs);
            }

            if (state != "notStarted")
            {
                UI.StatusDataGridView(rowIndex, fileName, "Not Available...", Color.Red);
                return false;
            }

            bool uploadSuccess = false;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                try
                {
                    using (var ssh = new SshClient(ipAddress, UI.ssh_port, UI.ssh_username, UI.ssh_password))
                    {
                        ssh.Connect();
                        var command = ssh.CreateCommand($"curl -F \"file=@{remoteTempPath}\" http://127.0.0.1/updater/upload --progress-bar");
                        var asyncResult = command.BeginExecute();

                        using (var reader = new StreamReader(command.OutputStream))
                        {
                            while (!asyncResult.IsCompleted)
                            {
                                var line = await reader.ReadLineAsync();
                                if (line != null && line.Contains("%"))
                                {
                                    UI.StatusDataGridView(rowIndex, fileName, $"Uploading Web... {line.Trim()}", Color.Yellow);
                                }
                                await Task.Delay(100);
                            }
                        }

                        command.EndExecute(asyncResult);
                        uploadSuccess = command.ExitStatus == 0;
                        ssh.Disconnect();
                        if (uploadSuccess) break;
                    }
                }
                catch
                {
                    if (attempt == MaxAttempts - 1)
                    {
                        SendCancelCommand(ipAddress, rowIndex, fileName);
                        return false;
                    }
                }
                await Task.Delay(DelayMs);
            }

            if (!uploadSuccess)
            {
                SendCancelCommand(ipAddress, rowIndex, fileName);
                UI.StatusDataGridView(rowIndex, fileName, "Web Upload Failed", Color.Red);
                return false;
            }

            bool installSuccess = false;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                try
                {
                    using (var ssh = new SshClient(ipAddress, UI.ssh_port, UI.ssh_username, UI.ssh_password))
                    {
                        ssh.Connect();
                        var command = ssh.RunCommand(
                            $"curl -X POST -H \"accept: text/plain\" -H \"Content-Type: multipart/form-data\" " +
                            $"-F \"dummy=\" \"http://127.0.0.1/updater/install\""
                        );
                        installSuccess = command.ExitStatus == 0;
                        ssh.Disconnect();
                        if (installSuccess) break;
                    }
                }
                catch
                {
                    if (attempt == MaxAttempts - 1)
                    {
                        SendCancelCommand(ipAddress, rowIndex, fileName);
                        return false;
                    }
                }
                await Task.Delay(DelayMs);
            }

            if (!installSuccess)
            {
                SendCancelCommand(ipAddress, rowIndex, fileName);
                UI.StatusDataGridView(rowIndex, fileName, "Install Failed", Color.Red);
                return false;
            }
            UI.StatusDataGridView(rowIndex, fileName, "Installed", Color.Lime);
            return true;
        }

        static bool CheckArchiveForDataJson(SshClient ssh, string remotePath)
        {
            try
            {
                var command = ssh.RunCommand($"tar -ztf {remotePath} | grep data.json");
                return command.ExitStatus == 0;
            }
            catch
            {
                return false;
            }
        }

        static bool ExtractArchive(SshClient ssh, string remotePath, string password)
        {
            try
            {
                var command = ssh.RunCommand($"echo '{password}' | sudo -S tar -xzf {remotePath} -C /");
                return command.ExitStatus == 0;
            }
            catch
            {
                return false;
            }
        }

        static async Task<bool> ProcessFileAsync(string ipAddress, string filePath, int rowIndex)
        {
            string fileName = Path.GetFileName(filePath);
            string remoteTempPath = $"/tmp/Upload/{fileName}";

            UI.StatusDataGridView(rowIndex, fileName, "Uploading...", Color.Yellow);
            if (!await UploadFileViaSftp(ipAddress, filePath, remoteTempPath, rowIndex, fileName))
                return false;            

            try
            {
                using (var ssh = new SshClient(ipAddress, UI.ssh_port, UI.ssh_username, UI.ssh_password))
                {
                    ssh.Connect();
                    string extension = Path.GetExtension(fileName).ToLower();

                    if (extension == ".tar.gz")
                    {
                        if (CheckArchiveForDataJson(ssh, remoteTempPath))
                        {
                            UI.StatusDataGridView(rowIndex, fileName, "Extracting...", Color.LightGreen);
                            if (ExtractArchive(ssh, remoteTempPath, UI.ssh_password))
                            {
                                UI.StatusDataGridView(rowIndex, fileName, "Extracted", Color.Lime);
                                return true;
                            }
                            return false;
                        }
                        else
                        {
                            UI.StatusDataGridView(rowIndex, fileName, "Installing via web...", Color.LightGreen);
                            return await ExecuteWebInstall(ipAddress, remoteTempPath, rowIndex, fileName);
                        }
                    }
                    else if (extension == ".deb")
                    {
                        UI.StatusDataGridView(rowIndex, fileName, "Installing package...", Color.LightGreen);
                        var command = ssh.RunCommand($"echo '{UI.ssh_password}' | sudo -S dpkg -i {remoteTempPath}");
                        return command.ExitStatus == 0;
                    }
                    else if (extension == ".sh")
                    {
                        UI.StatusDataGridView(rowIndex, fileName, "Running script...", Color.LightGreen);
                        var command = ssh.RunCommand($"chmod +x {remoteTempPath} && echo '{UI.ssh_password}' | sudo -S {remoteTempPath}");
                        return command.ExitStatus == 0;
                    }
                    else
                    {
                        UI.StatusDataGridView(rowIndex, fileName, "Unsupported file type", Color.Red);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                UI.StatusDataGridView(rowIndex, fileName, $"Processing Error: {ex.Message}", Color.Red);
                return false;
            }
        }

        public static async Task<bool> SingleFile(bool status, string ip, string file, int rowIndex)
        {
            if (!status)
            {
                UI.StatusDataGridView(rowIndex, Path.GetFileName(file), "Missed", Color.LightGray);
                UI.StepProgressBar();
                return false;
            }

            var result = await ProcessFileAsync(ip, file, rowIndex);
            UI.StepProgressBar();
            return result;
        }

        public static async Task<bool> MultipleFiles(bool status, string ip, string[] files, int rowIndex)
        {
            bool allSuccess = true;
            foreach (var file in files)
            {
                if (!status)
                {
                    UI.StatusDataGridView(rowIndex, Path.GetFileName(file), "Missed", Color.LightGray);
                    UI.StepProgressBar();
                    allSuccess = false;
                    continue;
                }

                var success = await ProcessFileAsync(ip, file, rowIndex);
                UI.StepProgressBar();
                if (!success) allSuccess = false;
            }
            return allSuccess;
        }
    }
}