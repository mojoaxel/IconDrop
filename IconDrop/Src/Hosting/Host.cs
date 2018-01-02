using System;
using System.Collections.Generic;
using System.Net;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SciterSharp;
using SciterSharp.Interop;
using IconDrop.Data;
using IconDrop.Svg;

namespace IconDrop.Hosting
{
	class Host : BaseHost
	{
		public Host(SciterWindow wnd)
		{
			var host = this;
			host.Setup(wnd);
			host.AttachEvh(new HostEvh());
			host.RegisterBehaviorHandler(typeof(IconsSource));
		}

		protected override SciterXDef.LoadResult OnLoadData(SciterXDef.SCN_LOAD_DATA sld)
		{
			if(sld.uri.StartsWith("svg:"))
			{
				if(sld.uri.Contains("?rnd="))
					sld.uri = sld.uri.Split('?')[0];
				int length = sld.uri.Length - 8;
				string hash = sld.uri.Substring(4, length);
				var icn = Joiner._iconsByHash[hash];
				switch(icn.kind)
				{
					case EIconKind.COLLECTION:
						try
						{
							byte[] bytess = File.ReadAllBytes(icn.path);
							_api.SciterDataReady(sld.hwnd, sld.uri, bytess, (uint)bytess.Length);
						}
						catch(Exception)
						{
						}
						break;

					case EIconKind.LIBRARY:
						string xml = SvgXML.FromIcon(icn).ToXML();
						byte[] bytes = Encoding.UTF8.GetBytes(xml);
						_api.SciterDataReady(sld.hwnd, sld.uri, bytes, (uint)bytes.Length);
						break;
				}
				return SciterXDef.LoadResult.LOAD_OK;
			}
			return base.OnLoadData(sld);
		}
		protected override void OnEngineDestroyed()
		{
#if WINDOWS
			Window.Dispose();
#endif
		}
	}

	class HostEvh : SciterEventHandler
	{
		private string _tmp_dir = Path.GetTempPath() + "IconDrop" + Path.DirectorySeparatorChar;

		public HostEvh()
		{
			if(Directory.Exists(_tmp_dir))
				Directory.Delete(_tmp_dir, true);
			Directory.CreateDirectory(_tmp_dir);
		}

		public bool Host_RevealFile(SciterElement el, SciterValue[] args, out SciterValue result)
		{
			string path = args[0].Get("");
#if OSX
			Process.Start("open", "-R \"" + path + '"');
#else
			Process.Start("explorer", "/select," + path.Replace('/', '\\'));
#endif

			result = null;
			return true;
		}

		public bool Host_RevealDir(SciterElement el, SciterValue[] args, out SciterValue result)
		{
			string path = args[0].Get("");
#if OSX
			Process.Start("open", path);
#else
			Process.Start("explorer", path.Replace('/', '\\'));
#endif
			result = null;
			return true;
		}

		public bool Host_Quit(SciterElement el, SciterValue[] args, out SciterValue result)
		{
			App.Exit();
			result = null;
			return true;
		}

		public bool Host_SetupCollections(SciterElement el, SciterValue[] args, out SciterValue result)
		{
			string dir = args[0].Get("");
			bool create_demo = args[1].Get(false);
			var cbk = args[2];
			Collections.Setup(dir, create_demo, cbk);
			
			result = null;
			return true;
		}

		public bool Host_CopySVGIconUse(SciterElement el, SciterValue[] args, out SciterValue result)
		{
			string name = args[0].Get("");
			string svguse = $"<svg class=\"icon icon-{name}\"><use xlink:href=\"#{name}\"></use></svg>";
			Utils.CopyText(svguse);
			result = null;
			return true;
		}

		public bool Host_CopySVGIconSymbol(SciterElement el, SciterValue[] args, out SciterValue result)
		{
			string hash = args[0].Get("");
			string ID = args.Length == 2 ? args[1].Get("") : "SOME-NAME-HERE";
			var icn = Joiner._iconsByHash[hash];
			Utils.CopyText(SvgSpriteXML.GetIconSymbolXML(icn, ID));
			result = null;
			return true;
		}

		public bool Host_SaveTempSVG(SciterElement el, SciterValue[] args, out SciterValue result)
		{
			string iconhash = args[0].Get("");
			bool white = args[1].Get(false);
			Icon icn = Joiner._iconsByHash[iconhash];

			string filepath;
			if(icn.kind == EIconKind.LIBRARY)
			{
				var svg = SvgXML.FromIcon(icn);
				svg.Scale(0.1f);
				var xml = svg.ToXML(white);

				filepath = _tmp_dir + icn.arr_tags[0] + ".svg";
				File.WriteAllText(filepath, xml);
			} else {
				filepath = icn.path;
			}

			Debug.Assert(File.Exists(filepath));
			result = new SciterValue(filepath);
			return true;
		}

		public bool Host_SavePNG(SciterElement el, SciterValue[] args, out SciterValue result)
		{
			string name = args[0].Get("");
			string filepath = _tmp_dir + name + ".png";
			var bytes = args[1].GetBytes();
			File.WriteAllBytes(filepath, bytes);

			result = new SciterValue(filepath);
			return true;
		}

		/*public bool Host_DaysToExpire(SciterElement el, SciterValue[] args, out SciterValue result)
		{
			result = new SciterValue(Ion.Ion.DaysToExpire);
			return true;
		}*/

		public bool Host_GenerateSVGSprite(SciterElement el, SciterValue[] args, out SciterValue result)
		{
			string sv_outputpath = args[0].Get("");
			var sv_icons = args[1];
			sv_icons.Isolate();

			var xml = new SvgSpriteXML();
			foreach(var item in sv_icons.Keys)
			{
				string hash = item.Get("");
				if(!Joiner._iconsByHash.ContainsKey(hash))
					continue;
				var icon = Joiner._iconsByHash[hash];
				icon.id = sv_icons[hash].Get("");
				xml.AddIcon(icon);
			}
			File.WriteAllText(sv_outputpath, xml.ToXML());

			result = null;
			return true;
		}

#if OSX
		public bool Host_StartDnD(SciterElement el, SciterValue[] args, out SciterValue result)
		{
			string file = args[0].Get("");
			int xView = args[1].Get(-1);
			int yView = args[2].Get(-1);
			new DnDOSX().StartDnD(file, xView, yView);

			result = null;
			return true;
		}
#endif
	}

	class BaseHost : SciterHost
	{
		protected static SciterX.ISciterAPI _api = SciterX.API;
		protected static SciterArchive _archive = new SciterArchive();
		protected SciterWindow _wnd;
		private static string _rescwd;

		static BaseHost()
		{
#if !DEBUG
			_archive.Open(SciterAppResource.ArchiveResource.resources);
#endif

#if DEBUG
			_rescwd = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location).Replace('\\', '/');
#if OSX
			_rescwd += "/../../../../../res/";
#else
			_rescwd += "/../../res/";
#endif
			_rescwd = Path.GetFullPath(_rescwd).Replace('\\', '/');
			Debug.Assert(Directory.Exists(_rescwd));
#endif
		}

		public void Setup(SciterWindow wnd)
		{
			_wnd = wnd;
			SetupWindow(wnd);
		}

		public void SetupPage(string page_from_res_folder)
		{
			string path = _rescwd + page_from_res_folder;
			Debug.Assert(File.Exists(path));

#if DEBUG
			string url = "file://" + path;
#else
			string url = "archive://app/" + page_from_res_folder;
#endif

			bool res = _wnd.LoadPage(url);
			Debug.Assert(res);
		}

		protected override SciterXDef.LoadResult OnLoadData(SciterXDef.SCN_LOAD_DATA sld)
		{
#if DEBUG
			if(sld.uri.StartsWith("file://"))
			{
				Debug.Assert(File.Exists(sld.uri.Substring(7)));
			}
#endif
			if(sld.uri.StartsWith("archive://app/"))
			{
				// load resource from SciterArchive
				string path = sld.uri.Substring(14);
				byte[] data = _archive.Get(path);
				if(data!=null)
					_api.SciterDataReady(sld.hwnd, sld.uri, data, (uint) data.Length);
			}

			// call base to ensure LibConsole is loaded
			return base.OnLoadData(sld);
		}

		public static byte[] LoadResource(string path)
		{
#if DEBUG
			path = _rescwd + path;
			Debug.Assert(File.Exists(path));
			return File.ReadAllBytes(path);
#else
			return _archive.Get(path);
#endif
		}
	}
}