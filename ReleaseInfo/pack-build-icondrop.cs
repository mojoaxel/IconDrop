//css_nuget FluentFTP
//css_ref System.IO.Compression
//css_ref System.IO.Compression.FileSystem
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using FluentFTP;

class Script
{
	const string CWD = @"D:\ProjetosSciter\IconDropMain\ReleaseInfo\"; // will be Environment.CurrentDirectory

	const string APPNAME = "IconDrop";
	const string APPNAME_EXE = APPNAME + ".exe";
	const string INSTALLER_EXE = APPNAME + "WIN.exe";


	static void Main(string[] args)
	{
		Environment.CurrentDirectory = CWD;

		Stopwatch sw = new Stopwatch();
		sw.Start();

		Console.WriteLine("#### " + APPNAME + " DEPLOY Tool ####");
		Console.WriteLine("0 - Release/Deploy");
		Console.WriteLine("1 - Test in Debug");
		Console.WriteLine("2 - Test in Release");
		Console.WriteLine("(default to 0)");

		string line = Console.ReadLine();

		bool test = false;
		string config = "Release";

		switch(line)
		{
			case "":
			case "0":
				break;

			case "1":
				config = "Debug";
				test = true;
				break;

			case "2":
				test = true;
				break;

			default:
				Console.WriteLine("Wrong argument, exiting..");
				Console.ReadLine();
				Environment.Exit(1);
				break;
		}
		
		GitPush();
		string outdir = Build(config, test);

		// run it
		SpawnProcess(outdir + APPNAME_EXE, "-test", false, false);
		
		// upload
		FtpClient client = new FtpClient("waws-prod-sn1-055.ftp.azurewebsites.windows.net");
		client.Credentials = new NetworkCredential("IconDrop\\$IconDrop", "JsNHRw9GabwmEMY8kFMcjutDk5d0DTc49Yv3qNbk9TCaprcMvqKFxzmBbDfh");
		client.Connect();
		client.UploadFile(CWD + INSTALLER_EXE, "/site/wwwroot/App_Data/Apps/" + INSTALLER_EXE, FtpExists.Overwrite, false, FtpVerify.None, new FtpReport());
		
		Console.WriteLine("\n\n----------------");
		Console.WriteLine("Done BUILD");
		Console.WriteLine("Took " + sw.ElapsedMilliseconds/1000 + "s");
		Console.Read();
	}
	
	class FtpReport : IProgress<double>
	{
		public void Report(double value)
		{
			Console.WriteLine(value);
		}
	}
	
	static void GitPush()
	{
		SpawnProcess("git", "status");
		SpawnProcess("git", "add -A");
		SpawnProcess("git", "commit -a -mAutoPush", true);
		SpawnProcess("git", "push");
	}
	
	static string Build(string CONFIG, bool TEST)
	{
		#region MSBuild
		if(true)
		{
			Console.WriteLine("### BUILD ###");

			string how = "Clean,Build";
			SpawnProcess(@"C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\msbuild.exe",
				CWD + $"..\\{APPNAME}\\{APPNAME}Windows.csproj /t:{how} /p:Configuration={CONFIG} /p:Platform=x64");
		}
		#endregion

		/*#region test
		if(TEST)
		{
			string[] test_names = new string[] { "TestGeneralSelf", "TestParser", "TestSemantic" };

			foreach(var name in test_names)
			{
				SpawnProcess(@"C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\msbuild.exe",
					@"..\" + name + '\\' + name + ".csproj /t:" + how + " /p:Configuration=" + CONFIG);

				SpawnProcess(@"C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe",
					@"..\" + name + @"\bin\" + CONFIG + '\\' + name + ".dll");
			}
		}
		#endregion*/

		#region pack/deploy
		if(!TEST)
		{
			Console.WriteLine("### PACK/DEPLOY ###");

			var WORK_DIR = $"{CWD}TmpInput\\";
			var CONFUSE_OUTDIR = $"{CWD}OutConfused\\";
			var CONFUSE_PROJ = "Confuse.crproj";

			// Delete and create these dirs
			if(Directory.Exists(WORK_DIR))
				Directory.Delete(WORK_DIR, true);
			Directory.CreateDirectory(WORK_DIR);

			if(Directory.Exists(CONFUSE_OUTDIR))
				Directory.Delete(CONFUSE_OUTDIR, true);
			Directory.CreateDirectory(CONFUSE_OUTDIR);

			// Copy \bin\Release to WORK_DIR
			string BIN_DIR = Path.GetFullPath(CWD + "..\\" + APPNAME + "\\bin\\Release");

			var files1 = Directory.EnumerateFiles(BIN_DIR, "*.exe", SearchOption.AllDirectories);
			var files2 = Directory.EnumerateFiles(BIN_DIR, "*.dll", SearchOption.AllDirectories);
			var files = files1
				.Union(files2)
				.ToList();
			foreach(var file in files)
			{
				string subpath = file.Substring(BIN_DIR.Length);
				string outpath = WORK_DIR + subpath;
				Directory.CreateDirectory(Path.GetDirectoryName(outpath));
				File.Copy(file, outpath);
			}

			// Copy \Shared to WORK_DIR
			string SHARED_DIR = Path.GetFullPath(CWD + "..\\" + APPNAME + "\\Shared");
			DirectoryCopy(SHARED_DIR, WORK_DIR + "\\Shared", true);

			// Confuse
			SpawnProcess(CWD + @"ConfuserEx_bin\Confuser.CLI.exe", $"-noPause {CONFUSE_PROJ}");

			// Copy confused files
			File.Copy(CONFUSE_OUTDIR + APPNAME_EXE, WORK_DIR + APPNAME_EXE, true);

			// Create installed
			if(CONFIG == "Release")
			{
				SpawnProcess("iscc", "installer.iss");
			}

			// Get version / Rename dir
			var ver = FileVersionInfo.GetVersionInfo(WORK_DIR + APPNAME_EXE);
			var dirname = CWD + $"v{ver.FileMajorPart}.{ver.FileMinorPart}\\";
			if(Directory.Exists(dirname))
				Directory.Delete(dirname, true);
			Directory.Move(WORK_DIR, dirname);

			return dirname;
		}

		return null;
		#endregion
	}


	static void SpawnProcess(string exe, string args, bool ignore_error = false, bool wait = true)
	{
		var startInfo = new ProcessStartInfo(exe, args)
		{
			FileName = exe,
			Arguments = args,
			UseShellExecute = false,
			WorkingDirectory = CWD
		};
		
		var p = Process.Start(startInfo);
		if(wait)
		{
			p.WaitForExit();
			
			if(p.ExitCode != 0 && ignore_error==false)
			{
				Console.ForegroundColor = ConsoleColor.Red;

				string msg = exe + ' ' + args;
				Console.WriteLine();
				Console.WriteLine("-------------------------");
				Console.WriteLine("FAILED: " + msg);
				Console.WriteLine("EXIT CODE: " + p.ExitCode);
				Console.WriteLine("Press ENTER to exit");
				Console.WriteLine("-------------------------");

				Console.ReadLine();
				Environment.Exit(0);
			}
		}
	}

	static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
	{
		// Get the subdirectories for the specified directory.
		DirectoryInfo dir = new DirectoryInfo(sourceDirName);

		if(!dir.Exists)
		{
			throw new DirectoryNotFoundException(
				"Source directory does not exist or could not be found: "
				+ sourceDirName);
		}

		DirectoryInfo[] dirs = dir.GetDirectories();
		// If the destination directory doesn't exist, create it.
		if(!Directory.Exists(destDirName))
		{
			Directory.CreateDirectory(destDirName);
		}

		// Get the files in the directory and copy them to the new location.
		FileInfo[] files = dir.GetFiles();
		foreach(FileInfo file in files)
		{
			string temppath = Path.Combine(destDirName, file.Name);
			file.CopyTo(temppath, false);
		}

		// If copying subdirectories, copy them and their contents to new location.
		if(copySubDirs)
		{
			foreach(DirectoryInfo subdir in dirs)
			{
				string temppath = Path.Combine(destDirName, subdir.Name);
				DirectoryCopy(subdir.FullName, temppath, copySubDirs);
			}
		}
	}
}