#define DEBUG

using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Tools.WindowsDevicePortal;
using static Microsoft.Tools.WindowsDevicePortal.DevicePortal;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ProspectUpload
{
    class FileModification
    {
        /// <summary>
        /// The device portal to which we are connecting.
        /// </summary>
        private static DevicePortal portal;

        /// <summary>
        /// An SSL thumbprint that we'll accept.
        /// </summary>
        private static string thumbprint;

        /// <summary>
        /// IP address, username, password, and file directory to upload.
        /// </summary>
        private static string ip;
        private static string username;
        private static string password;
        private static string fileDirectory;

        /// <summary>
        /// Directory of files to upload. 
        /// </summary>
        private static bool isCameraRoll = false;

        /// <summary>
        /// Prospect application package name.
        /// </summary>
        private static string pkgName = "prospect-hololens_1.0.0.0_x86__pzq3xp76mxafg";

        /// <summary>
        /// Prospect application appId.
        /// </summary>
        private static string appId;

        /// Error code meanings
        /// 1 = args issue
        /// 2 = connection issue
        /// 3 = file deletion issue
        /// 4 = file upload issue
        /// 5 = Prospect not on target device
        private static int Main(string[] args)
        {
            // settings for debug statements
            Debug.Listeners.Add(new TextWriterTraceListener(Console.Out));
            Debug.AutoFlush = true;
            Debug.Indent();

            // first check for right number of args, if wrong then print message and quit
            if (args.Length != 4)
            {
                Console.WriteLine(
                    "Incorrect number/order of arguments. Correct order is:  [ip address] [username] [password] [file directory].");
                return 1;
            }

            // uploading to camera roll?
            isCameraRoll = false;

            // properly format username and file directory, set other vars too
            ip = args[0];
            username = args[1].Substring(1, args[1].Length - 2);
            password = args[2];
            fileDirectory = args[3].Substring(1, args[3].Length - 2);

            // initialize portal
            portal = new DevicePortal(new DefaultDevicePortalConnection("https://" + ip, username, password));

            // first check to see if Prospect is installed on the targeted Hololens
            // if it isn't then quit with an error
            if (IsInstalled().Result == false)
            {
                Debug.WriteLine("Prospect is not on the targeted Hololens. Please install it and try again.");
                Environment.Exit(5);
            }

            // delete all files in Hololens directory and upload new files
            Debug.WriteLine("Connecting to Hololens...");
            ConnectToPortal().Wait();
            Debug.WriteLine("Deleting files...\n");
            DeleteFiles().Wait();
            Debug.WriteLine("Deleting complete!\n");
            Debug.WriteLine("Uploading files...\n");
            UploadFiles().Wait();
            Debug.WriteLine("Uploading complete!");

            // check if Prospect is running already, if so then terminate it
            TerminateIfIsRunning().Wait();

            // launch Prospect
            portal.LaunchApplicationAsync(appId, pkgName).Wait();
            return 0;
        }

        /// <summary>
        /// Handles unknown certificates.
        /// </summary>
        private static bool DoCertValidation(DevicePortal sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            X509Certificate2 cert = new X509Certificate2(certificate);
            thumbprint = cert.Thumbprint;
            return true;
        }

        /// <summary>
        /// Connect to DevicePortal.
        /// </summary>
        private static async Task<bool> ConnectToPortal()
        {
            // handle unidentified certs
            portal.UnvalidatedCert += DoCertValidation;

            // connect 
            portal.ConnectionStatus += (portal, connectArgs) =>
            {
                if (connectArgs.Status == DeviceConnectionStatus.Connected)
                {
                    Debug.Write("Connected to: ");
                    Debug.WriteLine(portal.Address);
                    Debug.Write("OS version: ");
                    Debug.WriteLine(portal.OperatingSystemVersion);
                    Debug.Write("Device family: ");
                    Debug.WriteLine(portal.DeviceFamily);
                    Debug.Write("Platform: ");
                    Debug.WriteLine(String.Format("{0} ({1})",
                        portal.PlatformName,
                        portal.Platform.ToString()));
                    Debug.WriteLine("\n");
                }
                else if (connectArgs.Status == DeviceConnectionStatus.Failed)
                {
                    Debug.WriteLine("Failed to connect to Hololens. Please ensure it is powered on and that you have enabled Developer Mode and Device Portal functionality in the settings.");
                    Debug.WriteLine(connectArgs.Message);
                    Environment.Exit(2);
                }
            };
            await portal.ConnectAsync();
            return true;
        }

        /// <summary>
        /// Checks to see if Prospect is on the device in question. Tests against full package name.
        /// </summary>
        private static async Task<bool> IsInstalled()
        {
            // get all packages on device
            AppPackages installedApps = await portal.GetInstalledAppPackagesAsync();

            // check if Prospect exists on device
            bool appExists = false;
            foreach (PackageInfo pkg in installedApps.Packages)
            {
                if (pkg.FullName.Equals(pkgName))
                {
                    appExists = true;
                    appId = pkg.AppId;
                }
            }
            return appExists;
        }

        /// <summary>
        /// Checks to see if Prospect is running on the device in question. If it is, quits out of it.
        /// </summary>
        private static async Task TerminateIfIsRunning()
        {
            // get all running processes on device
            RunningProcesses running = portal.GetRunningProcessesAsync().Result;

            // check if Prospect is running, if it is then quit out of it
            foreach (DeviceProcessInfo process in running.Processes)
            {
                // check if package name exists, if it is not an app then this will be null
                if (process.PackageFullName != null)
                {
                    if (process.PackageFullName.Equals(pkgName))
                    {
                        portal.TerminateApplicationAsync(process.PackageFullName).Wait();
                    }
                }
            }
        }

        /// <summary>
        /// Uploads individual files to the Hololens' CameraRoll or LocalCacheFolder.
        /// </summary>
        private static async Task UploadFile(string fileName)
        {
            if (isCameraRoll)
            {
                await portal.UploadFileAsync("CameraRoll", fileName);
            }
            else
            {
                await portal.UploadFileAsync("LocalAppData", fileName, "LocalCache", pkgName);
            }
            Debug.WriteLine("Uploaded " + fileName);
        }

        /// <summary>
        /// Uploads files to the application's LocalCacheFolder directory.
        /// </summary>
        private static async Task<bool> UploadFiles()
        {
            try
            {
                string[] files = Directory.GetFiles(fileDirectory, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    await UploadFile(files[i]);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to upload new model files. The Hololens hard drive may be full.");
                Debug.WriteLine(ex.GetType().ToString() + " - " + ex.Message);
                Environment.Exit(3);
            }
            return true;
        }

        /// <summary>
        /// Deletes contents of the CameraRoll or the application's LocalCacheFolder directory.
        /// </summary>
        private static async Task<bool> DeleteFiles()
        {
            try
            {
                var folderContents = new FolderContents();
                if (isCameraRoll)
                {
                    folderContents = await portal.GetFolderContentsAsync("CameraRoll");
                }
                else
                {
                    folderContents = await portal.GetFolderContentsAsync("LocalAppData", "LocalCache", pkgName);
                }
                foreach (FileOrFolderInformation FOFI in folderContents.Contents)
                {
                    await DeleteFile(FOFI.Name);
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to delete old model files.\n");
                Debug.WriteLine(ex.GetType().ToString() + " - " + ex.Message);
                Environment.Exit(4);
            }
            return true;
        }

        /// <summary>
        /// Delete individual files on the Hololens.
        /// </summary>
        private static async Task DeleteFile(string fileName)
        {
            if (!fileName.Equals("desktop.ini"))
            {
                if (isCameraRoll)
                {
                    await portal.DeleteFileAsync("CameraRoll", fileName);
                }
                else
                {
                    await portal.DeleteFileAsync("LocalAppData", fileName, "LocalCache", pkgName);
                }
                Debug.WriteLine("Deleted " + fileName);
            }
        }
    }
}
