using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System;
using Renci.SshNet;
using System.Threading;
using System.Linq;
using System.Web.Script.Serialization;

namespace Updater_2
{
    internal class Shh_UpdateFactor
    {
        private const int MaxAttempts = 5;
        private const int DelayMs = 500;

        private static async Task<string> ExecuteSshCommand(SshClient ssh, string command)
        {
            var cmd = ssh.RunCommand(command);
            return cmd.Result;
        }

        private static async Task<bool> CheckStateAsync(string ipAddress, int rowIndex, string fileName)
        {
            try
            {
                using (var ssh = new SshClient(ipAddress, UI.ssh_port, UI.ssh_username, UI.ssh_password))
                {
                    ssh.Connect();
                    string response = await ExecuteSshCommand(ssh,
                        $"curl -s http://{ipAddress}:{UI.web_port}/updater/state");

                    var serializer = new JavaScriptSerializer();
                    var stateObj = serializer.Deserialize<dynamic>(response);
                    string state = stateObj["stage"];

                    if (state != "notStarted")
                    {
                        UI.StatusDataGridView(rowIndex, fileName, "Not Available...", Color.Red);
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                UI.StatusDataGridView(rowIndex, fileName, $"State Check Error: {ex.Message}", Color.Red);
                return false;
            }
        }

        private static async Task<bool> UploadFileViaScp(string ipAddress, string localFilePath, string remotePath, int rowIndex, string fileName)
        {
            try
            {
                using (var scp = new ScpClient(ipAddress, UI.ssh_port, UI.ssh_username, UI.ssh_password))
                {
                    scp.Connect();
                    var fileInfo = new FileInfo(localFilePath);
                    long fileSize = fileInfo.Length;
                    long transferred = 0;

                    scp.Uploading += (sender, e) =>
                    {
                        transferred += e.Uploaded;
                        double progress = (double)transferred / fileSize * 100;
                        UI.StatusDataGridView(rowIndex, fileName, $"Uploading... {progress:F1}%", Color.Yellow);
                    };

                    await Task.Run(() => scp.Upload(fileInfo, remotePath));
                    return true;
                }
            }
            catch (Exception ex)
            {
                UI.StatusDataGridView(rowIndex, fileName, $"SCP Error: {ex.Message}", Color.Red);
                return false;
            }
        }

        private static async Task<bool> ExecuteWebInstall(string ipAddress, int rowIndex, string fileName)
        {
            try
            {
                using (var ssh = new SshClient(ipAddress, UI.ssh_port, UI.ssh_username, UI.ssh_password))
                {
                    ssh.Connect();
                    var command = ssh.CreateCommand(
                        $"curl -X POST -H \"accept: text/plain\" -H \"Content-Type: multipart/form-data\" " +
                        $"-F \"dummy=\" \"http://127.0.0.1/updater/install\""
                    );

                    var result = await Task.Factory.FromAsync(command.BeginExecute(), command.EndExecute);
                    return command.ExitStatus == 0;
                }
            }
            catch (Exception ex)
            {
                UI.StatusDataGridView(rowIndex, fileName, $"Install Error: {ex.Message}", Color.Red);
                return false;
            }
        }

        private static async Task<bool> CheckArchiveForDataJson(SshClient ssh, string remotePath)
        {
            var command = ssh.CreateCommand($"tar -ztf {remotePath} | grep data.json");
            var result = await Task.Factory.FromAsync(command.BeginExecute(), command.EndExecute);
            return command.ExitStatus == 0;
        }

        private static async Task<bool> ExtractArchive(SshClient ssh, string remotePath, string password)
        {
            var command = ssh.CreateCommand(
                $"echo '{password}' | sudo -S tar -xzf {remotePath} -C /"
            );
            var result = await Task.Factory.FromAsync(command.BeginExecute(), command.EndExecute);
            return command.ExitStatus == 0;
        }

        private static async Task<bool> ProcessFileAsync(string ipAddress, string filePath, int rowIndex)
        {
            string fileName = Path.GetFileName(filePath);
            string remoteTempPath = $"/tmp/Upload/{fileName}";

            // Upload file
            if (!await UploadFileViaScp(ipAddress, filePath, remoteTempPath, rowIndex, fileName))
                return false;

            try
            {
                using (var ssh = new SshClient(ipAddress, UI.ssh_port, UI.ssh_username, UI.ssh_password))
                {
                    ssh.Connect();
                    string extension = Path.GetExtension(fileName).ToLower();

                    if (extension == ".tar.gz")
                    {
                        if (await CheckArchiveForDataJson(ssh, remoteTempPath))
                        {
                            UI.StatusDataGridView(rowIndex, fileName, "Extracting...", Color.LightGreen);
                            if (await ExtractArchive(ssh, remoteTempPath, UI.ssh_password))
                            {
                                UI.StatusDataGridView(rowIndex, fileName, "Extracted", Color.Lime);
                                return true;
                            }
                            return false;
                        }
                        else
                        {
                            if (!await CheckStateAsync(ipAddress, rowIndex, fileName))
                                return false;

                            return await ExecuteWebInstall(ipAddress, rowIndex, fileName);
                        }
                    }
                    else if (extension == ".deb")
                    {
                        var command = ssh.CreateCommand(
                            $"echo '{UI.ssh_password}' | sudo -S dpkg -i {remoteTempPath}"
                        );
                        var result = await Task.Factory.FromAsync(command.BeginExecute(), command.EndExecute);
                        return command.ExitStatus == 0;
                    }
                    else if (extension == ".sh")
                    {
                        var command = ssh.CreateCommand(
                            $"chmod +x {remoteTempPath} && echo '{UI.ssh_password}' | sudo -S {remoteTempPath}"
                        );
                        var result = await Task.Factory.FromAsync(command.BeginExecute(), command.EndExecute);
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