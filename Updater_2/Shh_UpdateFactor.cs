using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Renci.SshNet;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;


namespace Updater_2
{
    internal class SshConnection : IDisposable
    {
        public SshClient Ssh { get; }
        public SftpClient Sftp { get; }
        private bool _isDisposed;

        public SshConnection(string host, int port, string username, string password)
        {
            Ssh = new SshClient(host, port, username, password);
            Sftp = new SftpClient(host, port, username, password);
        }

        public void Connect()
        {
            Ssh.Connect();
            Sftp.Connect();
        }

        public void Disconnect()
        {
            Sftp.Disconnect();
            Ssh.Disconnect();
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            Sftp?.Dispose();
            Ssh?.Dispose();

            _isDisposed = true;
        }
    }

    public static class SshCommandExtensions
    {
        public static IAsyncResult BeginExecute(this SshCommand command, Action<string> outputCallback, object state)
        {
            return command.BeginExecute(outputCallback, null, state);
        }

        public static IAsyncResult BeginExecute(this SshCommand command, Action<string> outputCallback, Action<string> errorCallback, object state)
        {
            return command.BeginExecute(
                delegate (IAsyncResult ar)
                {
                    using (var outputReader = new StreamReader(command.OutputStream))
                    using (var errorReader = new StreamReader(command.ExtendedOutputStream))
                    {
                        while (!ar.IsCompleted)
                        {
                            // Читаем вывод
                            while (outputReader.Peek() > -1)
                            {
                                var line = outputReader.ReadLine();
                                outputCallback?.Invoke(line);
                            }

                            // Читаем ошибки
                            while (errorReader.Peek() > -1)
                            {
                                var line = errorReader.ReadLine();
                                errorCallback?.Invoke(line);
                            }

                            Thread.Sleep(100);
                        }

                        // Читаем оставшиеся данные после завершения
                        while (outputReader.Peek() > -1)
                        {
                            var line = outputReader.ReadLine();
                            outputCallback?.Invoke(line);
                        }

                        while (errorReader.Peek() > -1)
                        {
                            var line = errorReader.ReadLine();
                            errorCallback?.Invoke(line);
                        }
                    }
                }, state);
        }
    }

    internal class Shh_UpdateFactor
    {
        const int maxUploadAttempts = 5;
        const int maxInstallAttempts = 5;
        const int DelayMs = 1000;

        static bool EnsureRemoteDirectoryExists(SshConnection connection, string remotePath)
        {
            try
            {
                var directoryPath = remotePath.Contains("/") ? remotePath.Substring(0, remotePath.LastIndexOf("/")) : "";

                if (string.IsNullOrEmpty(directoryPath))
                    return true;

                var mkdirCommand = connection.Ssh.RunCommand($"mkdir -p {directoryPath}");
                return mkdirCommand.ExitStatus == 0;
            }
            catch
            {
                return false;
            }
        }

        static async Task<bool> CheckArchiveForDataJson(SshConnection connection, string remotePath)
        {
            try
            {
                var command = connection.Ssh.RunCommand($"tar -ztf {remotePath} | grep -x '\\./data.json'");
                return command.ExitStatus == 0 && !string.IsNullOrWhiteSpace(command.Result);
            }
            catch
            {
                return false;
            }
        }

        static async Task<bool> UploadFileViaSftp(SshConnection connection, string localFilePath, string remotePath, int rowIndex, string fileName)
        {
            try
            {
                if (!EnsureRemoteDirectoryExists(connection, remotePath))
                {
                    UI.StatusDataGridView(rowIndex, fileName, "Failed to create remote directory", Color.Red);
                    return false;
                }

                using (var fileStream = new FileStream(localFilePath, FileMode.Open))
                {
                    var fileInfo = new FileInfo(localFilePath);
                    long fileSize = fileInfo.Length;
                    long transferred = 0;

                    var asyncResult = connection.Sftp.BeginUploadFile(fileStream, remotePath);

                    while (!asyncResult.IsCompleted)
                    {
                        await Task.Delay(100);
                        transferred = fileStream.Position;
                        double progress = (double)transferred / fileSize * 100;
                        UI.StatusDataGridView(rowIndex, fileName, $"Uploading... {progress:F1}%", Color.Yellow);
                    }

                    connection.Sftp.EndUploadFile(asyncResult);
                }

                return true;
            }
            catch (Exception ex)
            {
                UI.StatusDataGridView(rowIndex, fileName, $"Uploading Error: {ex.Message}", Color.Red);
                return false;
            }
        }

        static async Task<(string Stage, bool StageFinished, bool HasError)> GetState(SshConnection connection)
        {
            try
            {
                var command = connection.Ssh.RunCommand("curl -s http://127.0.0.1/updater/state");
                var response = command.Result;
                var serializer = new JavaScriptSerializer();
                dynamic json = serializer.Deserialize<dynamic>(response);

                return (
                    Stage: json["stage"] ?? "undefined",
                    StageFinished: json["stageFinished"] ?? false,
                    HasError: json["hasError"] ?? true
                );
            }
            catch
            {
                return ("undefined", false, true);
            }
        }

        static void SendCancelCommand(SshConnection connection)
        {
            try
            {
                connection.Ssh.RunCommand(
                    "curl -X POST -H \"accept: text/plain\" " +
                    "-H \"Content-Type: multipart/form-data\" " +
                    "-F \"dummy= \" \"http://127.0.0.1/updater/cancel\""
                );
            }
            catch
            {
            }
        }

        // Вспомогательные методы
        static async Task<bool> ResetStateToNotStarted(SshConnection connection, int rowIndex, string fileName)
        {
            for (int attempt = 0; attempt < maxInstallAttempts; attempt++)
            {
                var state = await GetState(connection);
                if (state.Stage == "notStarted" && !state.HasError)
                    return true;

                SendCancelCommand(connection);
                await Task.Delay(DelayMs);
            }

            UI.StatusDataGridView(rowIndex, fileName, "Not Available...", Color.Red);
            return false;
        }

        static async Task<bool> UploadFileWithProgress(SshConnection connection, string remoteTempPath, int rowIndex, string fileName)
        {
            UI.StatusDataGridView(rowIndex, fileName, $"Web Uploading...", Color.Yellow);

            try
            {
                using (var uploadCommand = connection.Ssh.CreateCommand($"curl -F \"file=@{remoteTempPath}\" http://127.0.0.1/updater/upload --progress-bar --no-buffer 2>&1"))
                {
                    var output = new StringBuilder();
                    var progressHandler = new Action<string>(line =>
                    {
                        if (line?.Contains("%") == true)
                        {
                            var percentMatch = Regex.Match(line.Trim(), @"\d+[.,]\d+%");
                            if (percentMatch.Success)
                            {
                                UI.StatusDataGridView(rowIndex, fileName, $"Web Uploading... {percentMatch.Value}", Color.Yellow);
                            }
                        }
                    });

                    // Запускаем команду асинхронно
                    var asyncResult = uploadCommand.BeginExecute(progressHandler, null);

                    // Ждем завершения с таймаутом
                    if (!asyncResult.AsyncWaitHandle.WaitOne(TimeSpan.FromMinutes(10)))
                    {
                        UI.StatusDataGridView(rowIndex, fileName, "Upload timeout", Color.Red);
                        uploadCommand.CancelAsync();
                        return false;
                    }

                    uploadCommand.EndExecute(asyncResult);
                    return uploadCommand.ExitStatus == 0;
                }
            }
            catch (Exception ex)
            {
                UI.StatusDataGridView(rowIndex, fileName, $"Upload Error: {ex.Message}", Color.Red);
                return false;
            }
        }

        static async Task<bool> WaitForUploadCompletion(SshConnection connection, int rowIndex, string fileName)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                var state = await GetState(connection);
                if (state.Stage == "uploading" && state.StageFinished && !state.HasError)
                    return true;

                await Task.Delay(5000);
            }

            UI.StatusDataGridView(rowIndex, fileName, "Upload Failed", Color.Red);
            return false;
        }

        static async Task<bool> TriggerInstallation(SshConnection connection, int rowIndex, string fileName)
        {
            UI.StatusDataGridView(rowIndex, fileName, "Installing...", Color.LightGreen);

            string url = $"http://127.0.0.1/updater/install?wipeSettings={UI.SaveSettings}";

            var installCommand = connection.Ssh.RunCommand(
                $"curl -X POST -H \"accept: text/plain\" -H \"Content-Type: multipart/form-data\" " +
                $"-F \"dummy=\" \"{url}\""
            );

            return installCommand.ExitStatus == 0;
            //return true;
        }

        static async Task<bool> WaitForInstallationCompletion(SshConnection connection, int rowIndex, string fileName)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                var state = await GetState(connection);
                if (state.Stage == "notStarted" && !state.StageFinished && !state.HasError)
                    return true;

                await Task.Delay(10000);
            }

            UI.StatusDataGridView(rowIndex, fileName, "Install Failed", Color.Red);
            return false;
        }

        static async Task<bool> ExecuteWebInstall(SshConnection connection, string remoteTempPath, int rowIndex, string fileName)
        {
            for (int uploadAttempt = 0; uploadAttempt < maxUploadAttempts; uploadAttempt++)
            {
                UI.StatusDataGridView(rowIndex, fileName, "Checking...", Color.Gray);

                // Сброс состояния перед попыткой
                if (!await ResetStateToNotStarted(connection, rowIndex, fileName))
                    continue;

                // Загрузка файла
                if (!await UploadFileWithProgress(connection, remoteTempPath, rowIndex, fileName))
                    continue;

                // Ожидание завершения загрузки на сервере
                if (!await WaitForUploadCompletion(connection, rowIndex, fileName))
                    continue;

                // Запуск установки
                if (!await TriggerInstallation(connection, rowIndex, fileName))
                    continue;

                // Ожидание завершения установки
                if (await WaitForInstallationCompletion(connection, rowIndex, fileName))
                {
                    UI.StatusDataGridView(rowIndex, fileName, "Installed!", Color.Lime);
                    return true;
                }
            }

            UI.StatusDataGridView(rowIndex, fileName, "All upload attempts failed", Color.Red);
            return false;
        }

        static async Task<bool> ProcessFileAsync(SshConnection connection, string filePath, int rowIndex)
        {
            string fileName = Path.GetFileName(filePath);
            string remoteTempPath = $"{UI.tmpUploadingPath}{fileName}";

            UI.StatusDataGridView(rowIndex, fileName, "Uploading...", Color.Yellow);
            if (!await UploadFileViaSftp(connection, filePath, remoteTempPath, rowIndex, fileName))
                return false;

            try
            {
                string extension = Path.GetExtension(fileName).ToLower();
                string fileNameLower = fileName.ToLower();

                if (fileNameLower.EndsWith(".tar.gz"))
                {
                    extension = ".tar.gz";
                }

                if (extension == ".tar.gz")
                {
                    UI.StatusDataGridView(rowIndex, fileName, "Archive or Installation package...", Color.Orange);
                    if (await CheckArchiveForDataJson(connection, remoteTempPath))
                    {
                        UI.StatusDataGridView(rowIndex, fileName, "Installing web...", Color.LightGreen);
                        return await ExecuteWebInstall(connection, remoteTempPath, rowIndex, fileName);
                    }
                    else
                    {
                        UI.StatusDataGridView(rowIndex, fileName, "Extracting...", Color.LightGreen);
                        return false;
                    }
                }
                else if (extension == ".deb")
                {
                    UI.StatusDataGridView(rowIndex, fileName, "Installing package...", Color.LightGreen);
                    var command = connection.Ssh.RunCommand($"echo '{UI.ssh_password}' | sudo -S dpkg -i {remoteTempPath}");
                    return command.ExitStatus == 0;
                }
                else if (extension == ".sh")
                {
                    UI.StatusDataGridView(rowIndex, fileName, "Running script...", Color.LightGreen);
                    var command = connection.Ssh.RunCommand($"chmod +x {remoteTempPath} && echo '{UI.ssh_password}' | sudo -S {remoteTempPath}");
                    return command.ExitStatus == 0;
                }
                else
                {
                    UI.StatusDataGridView(rowIndex, fileName, "Unsupported file type", Color.Red);
                    return false;
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

            using (var connection = new SshConnection(ip, UI.ssh_port, UI.ssh_username, UI.ssh_password))
            {
                connection.Connect();
                var result = await ProcessFileAsync(connection, file, rowIndex);
                UI.StepProgressBar();
                return result;
            }
        }

        public static async Task<bool> MultipleFiles(bool status, string ip, string[] files, int rowIndex)
        {
            bool allSuccess = true;

            using (var connection = new SshConnection(ip, UI.ssh_port, UI.ssh_username, UI.ssh_password))
            {
                connection.Connect();

                foreach (var file in files)
                {
                    if (!status)
                    {
                        UI.StatusDataGridView(rowIndex, Path.GetFileName(file), "Missed", Color.LightGray);
                        UI.StepProgressBar();
                        allSuccess = false;
                        continue;
                    }

                    var success = await ProcessFileAsync(connection, file, rowIndex);
                    UI.StepProgressBar();
                    if (!success) allSuccess = false;
                }
            }

            return allSuccess;
        }
    }
}