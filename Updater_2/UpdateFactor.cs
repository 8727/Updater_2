using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Threading;
using System.Drawing;
using Renci.SshNet;

namespace Updater_2
{
    internal class UpdateFactor
    {
        // Веб-версия методов
        static async Task<string> State_Async(string ipAddress)
        {
            string updateStatus = "undefined";
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(30);
                    using (var request = new HttpRequestMessage(new HttpMethod("GET"), $"http://{ipAddress}/updater/state"))
                    {
                        request.Headers.TryAddWithoutValidation("accept", "text/plain");

                        var response = await httpClient.SendAsync(request);
                        response.EnsureSuccessStatusCode();
                        var json = await response.Content.ReadAsStringAsync();
                        var datajson = new JavaScriptSerializer().Deserialize<dynamic>(json);
                        updateStatus = datajson["stage"];
                    }
                }
            }
            catch
            {
                updateStatus = "undefined";
            }

            return updateStatus;
        }

        static async Task<bool> Upload_Async(string ipAddress, string filePath)
        {
            bool updateStatus = false;
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(30);
                    var fileStream = File.OpenRead(filePath);
                    var request = new HttpRequestMessage
                    {
                        RequestUri = new Uri($"http://{ipAddress}/updater/upload"),
                        Method = HttpMethod.Post,
                        Content = new MultipartFormDataContent
                        {
                            {
                                new StreamContent(fileStream), "file", Path.GetFileName(filePath)
                            }
                        }
                    };
                    var response = await httpClient.SendAsync(request);
                    if (response.StatusCode.ToString() == "OK")
                    {
                        updateStatus = true;
                    }
                }
            }
            catch
            {
                updateStatus = false;
            }
            return updateStatus;
        }

        static async Task<bool> Install_Async(string ipAddress)
        {
            bool updateStatus = false;
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(15);
                    using (var request = new HttpRequestMessage(new HttpMethod("POST"), $"http://{ipAddress}/updater/install"))
                    {
                        request.Headers.TryAddWithoutValidation("accept", "text/plain");
                        request.Content = new StringContent("");
                        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");

                        var response = await httpClient.SendAsync(request);
                        if (response.StatusCode.ToString() == "OK")
                        {
                            updateStatus = true;
                        }
                    }
                }
            }
            catch
            {
                updateStatus = false;
            }
            return updateStatus;
        }

        static async Task<bool> Cancel_Async(string ipAddress)
        {
            bool updateStatus = false;
            try
            {
                using (var httpClient = new HttpClient())
                {
                    using (var request = new HttpRequestMessage(new HttpMethod("POST"), $"http://{ipAddress}/updater/cancel"))
                    {
                        request.Headers.TryAddWithoutValidation("accept", "*/*");

                        request.Content = new StringContent("");
                        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");

                        var response = await httpClient.SendAsync(request);
                    }
                }
            }
            catch
            {
                updateStatus = false;
            }
            return updateStatus;
        }

        // SSH-версия методов
        static async Task<bool> UploadViaScp(string ipAddress, string localFilePath, int rowIndex, string fileName)
        {
            try
            {
                using (var scp = new ScpClient(ipAddress, UI.ssh_port, UI.ssh_username, UI.ssh_password))
                {
                    scp.Connect();

                    // Get file info for progress calculation
                    var fileInfo = new FileInfo(localFilePath);
                    long fileSize = fileInfo.Length;
                    long transferred = 0;

                    // Setup progress handler
                    scp.Uploading += (sender, e) =>
                    {
                        transferred += e.Uploaded;
                        double progress = (double)transferred / fileSize * 100;
                        UI.StatusDataGridView(rowIndex, fileName, $"Loading... {progress:F1}%", Color.Yellow);
                    };

                    // Upload file to temporary location
                    string remoteTempPath = $"/tmp/Upload/{fileName}";
                    await Task.Run(() => scp.Upload(new FileInfo(localFilePath), remoteTempPath));

                    scp.Disconnect();
                    return true;
                }
            }
            catch (Exception ex)
            {
                UI.StatusDataGridView(rowIndex, fileName, $"SCP Error: {ex.Message}", Color.Red);
                return false;
            }
        }

        static async Task<bool> ExecuteCurlUpload(string ipAddress, string remoteFilePath, int rowIndex, string fileName)
        {
            try
            {
                using (var ssh = new SshClient(ipAddress, UI.ssh_port, UI.ssh_username, UI.ssh_password))
                {
                    ssh.Connect();

                    // Create and execute the curl command
                    var command = ssh.CreateCommand($"curl -F \"file=@{remoteFilePath}\" http://127.0.0.1/updater/upload --progress-bar");
                    var asyncResult = command.BeginExecute();

                    // Read output to track progress
                    using (var reader = new StreamReader(command.OutputStream))
                    {
                        while (!asyncResult.IsCompleted)
                        {
                            var line = await reader.ReadLineAsync();
                            if (line != null && line.Contains("%"))
                            {
                                UI.StatusDataGridView(rowIndex, fileName, $"Uploading: {line.Trim()}", Color.Yellow);
                            }
                            Thread.Sleep(100);
                        }
                    }

                    command.EndExecute(asyncResult);
                    ssh.Disconnect();

                    return command.ExitStatus == 0;
                }
            }
            catch (Exception ex)
            {
                UI.StatusDataGridView(rowIndex, fileName, $"Curl Error: {ex.Message}", Color.Red);
                return false;
            }
        }

        static async Task<bool> InstallViaSsh(string ipAddress, string filePath, int rowIndex, string fileName)
        {
            try
            {
                using (var ssh = new SshClient(ipAddress, UI.ssh_port, UI.ssh_username, UI.ssh_password))
                {
                    ssh.Connect();

                    string command;
                    string extension = Path.GetExtension(fileName).ToLower();

                    if (extension == ".tar.gz")
                    {
                        // Use web installer for tar.gz files
                        return await InstallWithRetry(ipAddress, 5);
                    }
                    else if (extension == ".deb")
                    {
                        // Install deb package
                        command = $"echo '{UI.ssh_password}' | sudo -S apt install -y {filePath}";
                    }
                    else if (extension == ".sh")
                    {
                        // Make script executable and run it
                        command = $"chmod +x {filePath} && echo '{UI.ssh_password}' | sudo -S {filePath}";
                    }
                    else
                    {
                        UI.StatusDataGridView(rowIndex, fileName, $"Unsupported file type: {extension}", Color.Red);
                        return false;
                    }

                    var result = ssh.RunCommand(command);
                    ssh.Disconnect();

                    if (result.ExitStatus == 0)
                    {
                        return true;
                    }
                    else
                    {
                        UI.StatusDataGridView(rowIndex, fileName, $"Install failed: {result.Error}", Color.Red);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                UI.StatusDataGridView(rowIndex, fileName, $"SSH Install Error: {ex.Message}", Color.Red);
                return false;
            }
        }

        // Общие методы
        private static async Task<bool> InstallWithRetry(string ip, int maxAttempts)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                bool installSuccess = await Install_Async(ip);
                if (installSuccess)
                    return true;

                if (attempt < maxAttempts - 1)
                    Thread.Sleep(500);
            }
            return false;
        }

        // Веб-версия обработки файла
        private static async Task<bool> ProcessFileWeb(bool status, string ip, string file, int rowIndex)
        {
            string fileName = Path.GetFileName(file);

            if (!status)
            {
                UI.StatusDataGridView(rowIndex, fileName, "Missed", Color.LightGray);
                UI.StepProgressBar();
                return false;
            }

            // Check device state
            UI.StatusDataGridView(rowIndex, fileName, "Check...", Color.Gray);
            string state = await State_Async(ip);

            if (state != "notStarted")
            {
                UI.StatusDataGridView(rowIndex, fileName, "Not Available...", Color.Red);
                UI.StepProgressBar();
                return true;
            }

            // Upload file
            UI.StatusDataGridView(rowIndex, fileName, "Uploading...", Color.Yellow);
            bool uploadSuccess = await Upload_Async(ip, file);

            if (!uploadSuccess)
            {
                UI.StatusDataGridView(rowIndex, fileName, "Upload Failed", Color.Red);
                UI.StepProgressBar();
                return true;
            }

            // Install
            UI.StatusDataGridView(rowIndex, fileName, "Install...", Color.LightGreen);
            bool installSuccess = await InstallWithRetry(ip, 5);

            if (!installSuccess)
            {
                UI.StatusDataGridView(rowIndex, fileName, "Install Failed", Color.Red);
                UI.StepProgressBar();
                return true;
            }

            UI.StatusDataGridView(rowIndex, fileName, "Installed", Color.Lime);
            UI.StepProgressBar();
            return false;
        }

        // SSH-версия обработки файла
        private static async Task<bool> ProcessFileSsh(bool status, string ip, string file, int rowIndex)
        {
            string fileName = Path.GetFileName(file);

            if (!status)
            {
                UI.StatusDataGridView(rowIndex, fileName, "Missed", Color.LightGray);
                UI.StepProgressBar();
                return false;
            }

            // Check device state (still using web API for state check)
            //UI.StatusDataGridView(rowIndex, fileName, "Check...", Color.Gray);
            //string state = await State_Async(ip);

            //if (state != "notStarted")
            //{
            //    UI.StatusDataGridView(rowIndex, fileName, "Not Available...", Color.Red);
            //    UI.StepProgressBar();
            //    return true;
            //}

            // Upload via SCP
            bool scpSuccess = await UploadViaScp(ip, file, rowIndex, fileName);
            if (!scpSuccess)
            {
                UI.StatusDataGridView(rowIndex, fileName, "SCP Upload Failed", Color.Red);
                UI.StepProgressBar();
                return true;
            }

            string remoteTempPath = $"/tmp/Upload/{fileName}";
            string extension = Path.GetExtension(fileName).ToLower();

            if (extension == ".tar.gz")
            {
                // Execute curl command for tar.gz files
                bool curlSuccess = await ExecuteCurlUpload(ip, remoteTempPath, rowIndex, fileName);
                if (!curlSuccess)
                {
                    UI.StatusDataGridView(rowIndex, fileName, "Curl Upload Failed", Color.Red);
                    UI.StepProgressBar();
                    return true;
                }
            }

            // Install
            UI.StatusDataGridView(rowIndex, fileName, "Install...", Color.LightGreen);
            bool installSuccess = await InstallViaSsh(ip, remoteTempPath, rowIndex, fileName);

            if (!installSuccess)
            {
                UI.StatusDataGridView(rowIndex, fileName, "Install Failed", Color.Red);
                UI.StepProgressBar();
                return true;
            }

            UI.StatusDataGridView(rowIndex, fileName, "Installed", Color.Lime);
            UI.StepProgressBar();
            return false;
        }

        // Публичные методы для веб-версии
        public static async Task<bool> SingleFileWeb(bool status, string ip, string file, int rowIndex)
        {
            return await ProcessFileWeb(status, ip, file, rowIndex);
        }

        public static async Task<bool> MultipleFilesWeb(bool status, string ip, string[] files, int startRowIndex)
        {
            bool hasError = false;

            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                bool fileError = await ProcessFileWeb(status, ip, file, startRowIndex + i);
                hasError = hasError || fileError;
            }

            return hasError;
        }

        // Публичные методы для SSH-версии
        public static async Task<bool> SingleFileSsh(bool status, string ip, string file, int rowIndex)
        {
            return await ProcessFileSsh(status, ip, file, rowIndex);
        }

        public static async Task<bool> MultipleFilesSsh(bool status, string ip, string[] files, int startRowIndex)
        {
            bool hasError = false;

            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                bool fileError = await ProcessFileSsh(status, ip, file, startRowIndex + i);
                hasError = hasError || fileError;
            }

            return hasError;
        }
    }
}