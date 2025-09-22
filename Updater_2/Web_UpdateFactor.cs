using System;
using System.IO;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Web.Script.Serialization;

namespace Updater_2
{
    internal class Web_UpdateFactor
    {
        private static readonly HttpClientHandler HttpClientHandler = new HttpClientHandler();
        private static readonly HttpClient HttpClient = new HttpClient(HttpClientHandler, false);
        private const int MaxAttempts = 5;
        private const int DelayMs = 500;

        static Web_UpdateFactor()
        {
            HttpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        private static async Task<string> GetStateAsync(string ipAddress)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, $"http://{ipAddress}:{UI.web_port}/updater/state"))
                {
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

                    using (var response = await HttpClient.SendAsync(request).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return new JavaScriptSerializer().Deserialize<dynamic>(json)["stage"];
                    }
                }
            }
            catch
            {
                return "undefined";
            }
        }

        private static async Task<bool> UploadFileAsync(string ipAddress, string filePath)
        {
            try
            {
                using (var fileStream = File.OpenRead(filePath))
                using (var content = new MultipartFormDataContent())
                {
                    content.Add(new StreamContent(fileStream), "file", Path.GetFileName(filePath));

                    using (var response = await HttpClient.PostAsync($"http://{ipAddress}:{UI.web_port}/updater/upload", content).ConfigureAwait(false))
                    {
                        return response.IsSuccessStatusCode;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> SendCommandAsync(string ipAddress, string command)
        {
            try
            {
                using (var content = new StringContent(""))
                {
                    content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");

                    using (var response = await HttpClient.PostAsync(
                        $"http://{ipAddress}:{UI.web_port}/updater/{command}",
                        content).ConfigureAwait(false))
                    {
                        return response.IsSuccessStatusCode;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> ProcessFileAsync(string ipAddress, string filePath, int rowIndex)
        {
            var fileName = Path.GetFileName(filePath);
            UI.StatusDataGridView(rowIndex, fileName, "Check...", Color.Gray);

            // Check state with retries
            string state;
            var attempts = MaxAttempts;
            do
            {
                state = await GetStateAsync(ipAddress).ConfigureAwait(false);
                if ((state == "undefined" || state == "uploading") && attempts > 0)
                {
                    await SendCommandAsync(ipAddress, "cancel").ConfigureAwait(false);
                    await Task.Delay(DelayMs).ConfigureAwait(false);
                }
                else
                {
                    break;
                }
            } while (attempts-- > 0);

            if (state != "notStarted")
            {
                UI.StatusDataGridView(rowIndex, fileName, "Not Available...", Color.Red);
                return false;
            }

            // Upload file with retries
            UI.StatusDataGridView(rowIndex, fileName, "Uploading...", Color.Yellow);
            attempts = MaxAttempts;
            bool success;
            do
            {
                success = await UploadFileAsync(ipAddress, filePath).ConfigureAwait(false);
                if (!success && attempts > 0)
                {
                    await SendCommandAsync(ipAddress, "cancel").ConfigureAwait(false);
                    await Task.Delay(DelayMs).ConfigureAwait(false);
                }
                else
                {
                    break;
                }
            } while (attempts-- > 0);

            if (!success)
            {
                UI.StatusDataGridView(rowIndex, fileName, "Upload Failed", Color.Red);
                return false;
            }

            // Install with retries
            UI.StatusDataGridView(rowIndex, fileName, "Install...", Color.LightGreen);
            attempts = MaxAttempts;
            do
            {
                success = await SendCommandAsync(ipAddress, "install").ConfigureAwait(false);
                if (!success && attempts > 0)
                {
                    await SendCommandAsync(ipAddress, "cancel").ConfigureAwait(false);
                    await Task.Delay(DelayMs).ConfigureAwait(false);
                }
                else
                {
                    break;
                }
            } while (attempts-- > 0);

            if (!success)
            {
                UI.StatusDataGridView(rowIndex, fileName, "Install Failed", Color.Red);
                return false;
            }

            UI.StatusDataGridView(rowIndex, fileName, "Installed", Color.Lime);
            return true;
        }

        public static async Task<bool> SingleFile(bool status, string ip, string file, int rowIndex)
        {
            if (!status)
            {
                UI.StatusDataGridView(rowIndex, Path.GetFileName(file), "Missed", Color.LightGray);
                UI.StepProgressBar();
                return false;
            }

            var result = await ProcessFileAsync(ip, file, rowIndex).ConfigureAwait(false);
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

                var success = await ProcessFileAsync(ip, file, rowIndex).ConfigureAwait(false);
                UI.StepProgressBar();
                if (!success) allSuccess = false;
            }
            return allSuccess;
        }

    }
}
