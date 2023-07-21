using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
//using System.CodeDom.Compiler;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
//using OpenQA.Selenium.Support.UI;

namespace geoipdl
{
    internal class Program
    {
        static bool _headless = false;
        static bool _pauseOnError = false;
        static EdgeDriver _driver = null;
        static readonly string _exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        static readonly string _driverExe = Path.Combine(_exeDir, "msedgedriver.exe");
        static readonly string _driverZip = Path.Combine(_exeDir, "edgedriver_win64.zip");
        static readonly string _usersFolder = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        static readonly string _edgeUserDataDir = Path.Combine(_usersFolder, $@"{Environment.UserName}\AppData\Local\Microsoft\Edge\User Data");
        static readonly string _edgeProfileName = "Selenium";

        static readonly string _dlUrl = @"https://gitlab.torproject.org/tpo/network-health/metrics/geoip-data/-/packages";
        static readonly string _dlZip = "geoip.zip";
        static readonly string[] _dlFiles = { "geoip", "geoip6" };
        static string _outDir = ".";
        static int _dlInterval = 0;

        static async Task Main(string[] args)
        {
            //var cp = new CompilerParameters();
            //cp.CompilerOptions = "/optimize /target:winexe /win32icon:icon-reconnect.ico";

            try
            {
                Directory.SetCurrentDirectory(_exeDir);

                foreach (string arg in args)
                {
                    if (arg.ToLower() == "--headless")
                        _headless = true;
                    if (arg.ToLower() == "--pause-on-error")
                        _pauseOnError = true;
                    if (arg.ToLower().Contains("--output-folder="))
                    {
                        _outDir = arg.Substring(arg.IndexOf("=") + 1);
                        _outDir = _outDir.Trim().Trim('"');
                    }
                    if (arg.ToLower().Contains("--period-days="))
                    {
                        var str = arg.Substring(arg.IndexOf("=") + 1).Trim();
                        _dlInterval = int.Parse(str);
                    }
                }

                if (_dlInterval > 0)
                {
                    var filePath = @"TimeStamp.txt";
                    var needSave = false;
                    DateTime currentDate = DateTime.Now;

                    if (File.Exists(filePath))
                    {
                        // Read date from file
                        DateTime savedDate;
                        using (StreamReader reader = new StreamReader(filePath))
                        {
                            savedDate = DateTime.Parse(reader.ReadLine(), CultureInfo.InvariantCulture);
                        }
                        // Compare dates
                        TimeSpan timeSpan = currentDate.Subtract(savedDate);
                        if (timeSpan.Days >= _dlInterval)
                        {
                            needSave = true;
                            Console.WriteLine($"{_dlInterval} or more days have passed since the saved date.");
                        }
                        else
                        {
                            //Console.WriteLine($"Less than {_dlInterval} days have passed since the saved date.");
                            Environment.Exit(0);
                        }
                    }
                    else
                    {
                        needSave = true;
                    }
                    // Save current date to file
                    if (needSave)
                    {
                        using (StreamWriter writer = new StreamWriter(filePath))
                        {
                            writer.WriteLine(currentDate.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }

                // register Ctrl+C handler
                Console.CancelKeyPress += (sender, e) =>
                {
                    //e.Cancel = true;  // Cancel the termination process
                    Console.WriteLine(">> Ctrl+C pressed");
                    KillDriver();
                };

                // Print header
                System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                Console.Title = fvi.ProductName;
                Console.WriteLine("=====================================================================");
                Console.WriteLine(fvi.ProductName + " v" + fvi.FileVersion);
                Console.WriteLine("=====================================================================");
                Console.WriteLine("Download webdriver of the same version as installed Edge browser");
                Console.WriteLine("https://developer.microsoft.com/en-us/microsoft-edge/tools/webdriver/");
                Console.WriteLine("=====================================================================");
                Console.WriteLine("--headless");
                Console.WriteLine("--pause-on-error");
                Console.WriteLine(@"--output-folder=""C:\GEOIP\FOLDER""");
                Console.WriteLine("--period-days=<DOWNLOAD_INTERVAL>");
                Console.WriteLine("=====================================================================");
                Console.WriteLine("Return errorlevel=0 - success, 1 - error");
                Console.WriteLine("=====================================================================");
                Console.WriteLine($"Edge user data dir: {_edgeUserDataDir}");
                Console.WriteLine($"Edge profile:       {_edgeProfileName}");
                Console.WriteLine($"Edge driver:        {_driverExe}");
                Console.WriteLine($"Current directory:  {Directory.GetCurrentDirectory()}");
                Console.WriteLine($"Output folder:      {_outDir}");
                Console.WriteLine();

                if (!Directory.Exists(_outDir)) throw new ArgumentException("--output-folder does not exist");

                UpdateDriver();
                DoMain().GetAwaiter().GetResult();
                KillDriver();
            }
            catch (Exception ex)
            {
                KillDriver();
                Console.WriteLine($"{ex.GetType()}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
                Console.Out.Flush();
                if (_pauseOnError) while (true) await Task.Delay(1000);
                Environment.Exit(1);
            }
            Environment.Exit(0);
        }

        private static async Task DoMain()
        {
            EdgeOptions edgeOptions = new EdgeOptions();
            // Here you set the path of the profile ending with User Data not the profile folder
            edgeOptions.AddArgument($"--user-data-dir={_edgeUserDataDir}");
            // Here you specify the actual profile folder. If it is Default profile, no need to use this line of code
            edgeOptions.AddArgument($"--profile-directory={_edgeProfileName}");
            if (_headless)
                edgeOptions.AddArgument("headless");

            _driver = new EdgeDriver(edgeOptions);
            _driver.Manage().Window.Maximize();
            _driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(6); // implicit timeout

            // go to package page
            Console.WriteLine(">> Open site...");
            DriverGo(_dlUrl);
            var element = _driver.FindElement(By.CssSelector("a.gl-text-body.gl-min-w-0"));
            DriverGo(element.GetAttribute("href"));

            // find file to download
            element = null;
            var elements = _driver.FindElements(By.CssSelector("[href*='/-/package_files/']"));
            foreach (var el in elements)
            {
                var children = el.FindElements(By.TagName("span"));
                foreach (var e in children)
                {
                    var input = e.Text;
                    if (Regex.IsMatch(input, @"^geoip-\w{16}\.jar$"))
                    {
                        Console.WriteLine(e.Text);
                        //Console.WriteLine(e.GetAttribute("innerHTML"));
                        element = el;
                        goto found_out;
                    }
                }
            }
        found_out:
            if (element == null) throw new Exception("Download link not found");

            RunBackgroundConsoleProcess("wget.exe", $@"-O {_dlZip} {element.GetAttribute("href")}");

            foreach (var file in _dlFiles) if (File.Exists(file)) File.Delete(file);
            ExtractFiles(_dlZip, _dlFiles);
            foreach (var file in _dlFiles) if (!File.Exists(file)) throw new FileNotFoundException($@"File '{file}' is not found'");

            if (_outDir != ".")
                foreach (var file in _dlFiles)
                    File.Copy(file, Path.Combine(_outDir, file), true);

            Console.WriteLine(">> OK"); Console.Out.Flush();
            await Task.Delay(1500);
        }

        private static void ExtractFiles(string zipFile, string[] filenames)
        {
            using (var zip = ZipFile.OpenRead(zipFile))
            {
                var len = 0;
                foreach (string filename in filenames) if (filename.Length > len) len = filename.Length;
                len++;
                foreach (string filename in filenames)
                {
                    zip.GetEntry(filename)?.ExtractToFile(filename, true);
                    Console.WriteLine($"{filename}{new string(' ', len - filename.Length)}{File.GetLastWriteTime(filename).ToShortDateString()}");
                }
            }
        }

        private static void DriverGo(string url)
        {
            Console.WriteLine(url);
            _driver.Url = url;
        }

        private static void RunBackgroundConsoleProcess(string exe, string args)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = exe;
            startInfo.Arguments = args;
            startInfo.UseShellExecute = false;  // Required to redirect the standard output and error
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            // Start the process
            Process process = new Process();
            process.StartInfo = startInfo;
            process.OutputDataReceived += Process_OutputDataReceived;
            process.ErrorDataReceived += Process_ErrorDataReceived;

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for the process to end
            process.WaitForExit();
            process.Close();
            process.Dispose();
        }
        private static void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                Console.WriteLine(e.Data);
            }
        }
        private static void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                Console.WriteLine(e.Data);
            }
        }

        private static void KillDriver()
        {
            if (_driver != null)
            {
                _driver.Quit();
                _driver.Dispose();
                _driver = null;
            }
        }

        private static void UpdateDriver()
        {
            string edgeVer = GetEdgeVersion();
            string driverVer = GetFileVersion(_driverExe);
            Console.WriteLine($"Edge version:   {edgeVer}");
            Console.WriteLine($"Driver version: {driverVer}");
            if (string.IsNullOrEmpty(edgeVer)) throw new Exception("Microsoft Edge is not installed");
            if (edgeVer == driverVer) return;

            // download driver
            string driverUrl = GetDriverUrl(GetEdgeVersion());
            Console.WriteLine($"Downloading driver");
            Console.Write($"{driverUrl} ...");
            DownloadFile(driverUrl, _driverZip);
            Console.WriteLine();
            if (File.Exists(_driverExe)) File.Delete(_driverExe);
            ZipFile.ExtractToDirectory(_driverZip, Path.GetDirectoryName(_driverExe));
        }

        /* Returns null if file not found*/
        private static string GetFileVersion(string filePath)
        {
            string version = null;
            if (File.Exists(filePath)) version = FileVersionInfo.GetVersionInfo(filePath).FileVersion;
            return version;
        }

        /* Returns null if keys not found*/
        private static string GetEdgeVersion()
        {
            var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Edge\BLBeacon");
            return reg?.GetValue("version")?.ToString();
        }

        private static string GetDriverUrl(string edgeVersion)
        {
            return $@"https://msedgedriver.azureedge.net/{edgeVersion}/edgedriver_win64.zip";
        }

        private static void DownloadFile(string url, string savePath)
        {
            using (var client = new System.Net.WebClient())
            {
                client.DownloadFile(url, savePath);
            }
        }

    }
}
