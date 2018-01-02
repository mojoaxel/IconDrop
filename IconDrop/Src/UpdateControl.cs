using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using SciterSharp;
using IconDrop.Data;

namespace IconDrop
{
	class UpdateInfo
	{
		public int v = 0;
		public int n = 0;// NTP
		public int r = 0;// last update date
	}

	static class UpdateControl
	{
		private static string PathUpdateInfo = Consts.DirUserData + "update.json";

		public static void Setup()
		{
#if DEBUG
			//File.Delete(PathUpdateInfo);
#endif

			new Thread(() =>
			{
				while(true)
				{
					try
					{
						string str = new WebClient().DownloadString(Consts.SERVER_ION + "Ion/Info");
						if(str.ToLower().IndexOf("<html>") == -1)
						{
							File.WriteAllText(PathUpdateInfo, str);
							return;
						}
					}
					catch(Exception ex)
					{
						//Debug.Assert(false);
						Thread.Sleep(TimeSpan.FromMinutes(5));
					}
				}
			}).Start();
		}

		public static DateTime GetDateTime()
		{
			if(!File.Exists(PathUpdateInfo))
				return DateTime.Now;

			SciterValue sv = SciterValue.FromJSONString(File.ReadAllText(PathUpdateInfo));
			int epoch = sv["n"].Get(0);
			DateTime dt_ntp = Utils.FromUnixTime(epoch);
			if(DateTime.UtcNow > dt_ntp)
				dt_ntp = DateTime.UtcNow;
			return dt_ntp;
		}

		public static (bool, string) IsUpdateAvailable()
		{
			if(!File.Exists(PathUpdateInfo))
				return (false, null);

			SciterValue sv = SciterValue.FromJSONString(File.ReadAllText(PathUpdateInfo));
			UpdateInfo upinfo = new UpdateInfo()
			{
				v = sv["v"].Get(0),
				n = sv["n"].Get(0),
				r = sv["r"].Get(0),
			};

			int major = upinfo.v >> 16;
			int minor = upinfo.v & 0xFFFF;
			string lastVersion = major + "." + minor;

			if(Consts.VersionInt < upinfo.v)
				return (false, lastVersion);
			return (false, null);
		}
	}
}