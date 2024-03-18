using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

internal class Program
{
    private async static Task Main(string[] args)
    {
        try
        {
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            var settings = JsonSerializer.Deserialize<SetupSettings>(File.ReadAllText(settingsPath));
            if(settings.LatestDownloadedUpdateDate == null)
            {
                settings.LatestDownloadedUpdateDate = DateTime.MinValue;
            }
            if(settings.DoNotDeleteTheseFiles == null)
            {
                settings.DoNotDeleteTheseFiles = "";
            }
            string GitHubApiBaseUrl = "https://api.github.com/repos/";
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "ShipThatLauncher");
            var apiUrl = $"{GitHubApiBaseUrl}{settings.GithubOwner}/{settings.GithubRepo}/releases";
            Console.WriteLine($"Checking For Updates At: {apiUrl}");
            var response = await httpClient.GetAsync(apiUrl);
            Console.WriteLine($"Reposnse Was {response.StatusCode.ToString()}");
            if(response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var releases = JsonSerializer.Deserialize<Root[]>(content);
                Console.WriteLine($"{releases.Length} Releases Found!");
                // Check all assests for one that has the zip name so we can do stuff
                // like CUDA and CPU released and avoid source code zips

                // Make this a local function so we can bail out of the nested loops gracefully
                async Task FindZipAndExtract()
                {
                    foreach(var rel in releases.Where(a => !a.draft))
                    {
                        Console.WriteLine($"Checking Release: {rel.name}");

                        if(settings.LatestDownloadedUpdateDate >= rel.published_at)
                        {
                            continue;
                        }

                        foreach(var ass in rel.assets)
                        {
                            if(!ass.browser_download_url.EndsWith(settings.ZipName))
                            {
                                continue;
                            }

                            Console.WriteLine($"Found it!  Downloading {ass.browser_download_url}...");
                            var downloadResponse = await httpClient.GetAsync(ass.browser_download_url);
                            if(downloadResponse.IsSuccessStatusCode)
                            {
                                var downloadStream = await downloadResponse.Content.ReadAsStreamAsync();
                                var downloadPath = Path.Combine(Path.GetTempPath(),
                                    Guid.NewGuid().ToString() + ".zip");
                                using(var fileStream = new FileStream(downloadPath, FileMode.Create,
                                    FileAccess.Write, FileShare.None))
                                {
                                    await downloadStream.CopyToAsync(fileStream);
                                }
                                var extractPath = AppContext.BaseDirectory;

                                // Ok at this stage we're confident we've got a clean download && we're
                                // about to install it.  We want to clean out the folder first so if 
                                // they stopped using something we don't want to keep it around
                                // but we do want to keep things that should survive the update like 
                                // user settings and such
                                var filesToDelete = Directory.GetFiles(extractPath,
                                    "*.*", SearchOption.AllDirectories);
                                var filesToKeep = settings.DoNotDeleteTheseFiles.Split(';', 
                                    StringSplitOptions.RemoveEmptyEntries);
                                Console.WriteLine("Cleaning Up Old Install...");
                                foreach(var item in filesToDelete)
                                {
                                    var fi = new FileInfo(item);
                                    if(fi.Name != "launcher.exe" && 
                                        !filesToKeep.Contains(fi.Name))
                                    {
                                        Console.WriteLine($"Deleting {fi.FullName}");
                                        fi.Delete();
                                    }
                                }

                                var zip = ZipFile.OpenRead(downloadPath);
                                foreach(var item in zip.Entries)
                                {
                                    Console.WriteLine($"Extracting {item.FullName}");
                                    var destPath = Path.GetFullPath(Path.Combine(extractPath, item.FullName));
                                    if(!Directory.Exists(Path.GetDirectoryName(destPath)))
                                    {
                                        Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                                    }
                                    if(item.Name != "" && item.Name != "launcher.exe")
                                    {
                                        item.ExtractToFile(destPath, true);
                                    }
                                }

                                zip.Dispose();
                                File.Delete(downloadPath);
                                settings.LatestDownloadedUpdateDate = rel.published_at;
                                File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));
                            }
                            return;
                        }
                    }
                }

                await FindZipAndExtract();
            }

            Console.WriteLine($"Executing: {settings.ExeFileName}");
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = settings.ExeFileName,
                UseShellExecute = false,
            };
            using Process process = new Process { StartInfo = startInfo };
            process.Start();
        }
        catch(Exception ex)
        {
            Console.WriteLine(ex.ToString());
            Console.ReadKey();
        }
    }
}

public class Asset
{
    public string browser_download_url { get; set; }
}

public class Root
{
    public bool draft { get; set; }
    public bool prerelease { get; set; }
    public DateTime created_at { get; set; }
    public DateTime published_at { get; set; }
    public List<Asset> assets { get; set; }
    public string name { get; set; }
}

public class SetupSettings
{
    public string Title { get; set; }
    public string DefaultInstallFolderName { get; set; }
    public string ExeFileName { get; set; }
    public string IconFileName { get; set; }
    public bool UseLauncher { get; set; }
    public string GithubOwner { get; set; }
    public string GithubRepo { get; set; }
    public string ZipName { get; set; }
    public DateTime? LatestDownloadedUpdateDate { get; set; }
    public string DoNotDeleteTheseFiles { get; set; }
}