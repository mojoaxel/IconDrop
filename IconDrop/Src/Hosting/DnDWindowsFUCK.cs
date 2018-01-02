#if false
//https://www.codeproject.com/Articles/840/How-to-Implement-Drag-and-Drop-Between-Your-Progra
//https://msdn.microsoft.com/en-us/library/windows/desktop/bb776902(v=vs.85).aspx
//https://www.codeproject.com/Articles/15576/How-to-drag-a-virtual-file-from-your-app-into-Wind

using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using SciterSharp.Interop;
using System.Collections.Specialized;
using System.Runtime.InteropServices.ComTypes;

namespace IconDrop.Hosting
{
	enum DROPEFFECT : int
	{
		DROPEFFECT_NONE,
		DROPEFFECT_COPY,
		DROPEFFECT_MOVE,
		DROPEFFECT_LINK,
		DROPEFFECT_SCROLL
	}

	enum CONTROL : int
	{
		MK_CONTROL = 0x0008,
		MK_SHIFT = 0x0004,
		MK_ALT = 0x20,
		MK_LBUTTON = 0x0001,
		MK_MBUTTON = 0x0010,
		MK_RBUTTON = 0x0002
	}

	[ComImport, Guid("00000121-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	interface IDropSource
	{
		[PreserveSig]
		uint QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool fEscapePressed, CONTROL grfKeyState);

		[PreserveSig]
		uint GiveFeedback(DROPEFFECT dwEffect);
	}

	class DnDWindows : IDropSource
	{
		public static FileDataObject _dataObject;

		public static void CopySVG()
		{
		}

		public void StartDnd(string svgpath, Action<bool> f_dropped)
		{
			//var tmpfile = Path.GetTempFileName() + ".svg";
			var tmpfile = "D:\\Group.svg";

			_dataObject = new FileDataObject(tmpfile);

			int ret;
			int res = DoDragDrop(_dataObject, this, (int)DROPEFFECT.DROPEFFECT_COPY, out ret);

			_dataObject.ClearStorage();
		}

#region PInvoke
		[DllImport("ole32.dll")]
		static extern int DoDragDrop(System.Runtime.InteropServices.ComTypes.IDataObject pDataObject, IDropSource pDropSource, int dwOKEffect, out int pdwEffect);

		[DllImport("user32.dll")]
		static extern IntPtr SetCursor(IntPtr hCursor);

		const uint S_OK = 0;
		const uint DRAGDROP_S_DROP = 0x00040100;
		const uint DRAGDROP_S_CANCEL = 0x00040101;
		const uint DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;
		const uint E_UNSPEC = 0x80004005;
#endregion

#region IDropSource
		public uint QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool fEscapePressed, CONTROL grfKeyState)
		{
			if(!grfKeyState.HasFlag(CONTROL.MK_LBUTTON))
			{
				return DRAGDROP_S_DROP;
			}
			return S_OK;
		}

		public uint GiveFeedback(DROPEFFECT dwEffect)
		{
			return DRAGDROP_S_USEDEFAULTCURSORS;
		}
#endregion
	}

	class FileDataObject : System.Runtime.InteropServices.ComTypes.IDataObject
	{
		#region COM constants
		private const int S_OK = 0;
		private const int S_FALSE = 1;
		private const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);
		private const int DV_E_FORMATETC = unchecked((int)0x80040064);
		private const int DV_E_TYMED = unchecked((int)0x80040069);
		private const int DV_E_CLIPFORMAT = unchecked((int)0x8004006A);
		private const int DV_E_DVASPECT = unchecked((int)0x8004006B);
		#endregion

		#region Unmanaged functions
		// These are helper functions for managing STGMEDIUM structures
		[DllImport("urlmon.dll")]
		private static extern int CopyStgMedium(ref STGMEDIUM pcstgmedSrc, ref STGMEDIUM pstgmedDest);

		[DllImport("ole32.dll")]
		private static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);
		#endregion

		private IList<KeyValuePair<FORMATETC, STGMEDIUM>> _storage = new List<KeyValuePair<FORMATETC, STGMEDIUM>>();

		[StructLayout(LayoutKind.Sequential)]
		struct DROPFILES
		{
			int pFiles;
			PInvokeUtils.POINT pt;
			int fNC;
			int fWide;
		}

		public FileDataObject(string path)
		{
			Debug.Assert(path.Length != 0);

			IntPtr hglobal;

			// Create DROPFILES
			using(var ms = new MemoryStream())
			{
				BinaryWriter bw = new BinaryWriter(ms);

				// DROPFILES.pFiles
				bw.Write(Marshal.SizeOf<DROPFILES>());

				// DROPFILES.pt
				bw.Write((int)8);
				bw.Write((int)8);

				// DROPFILES.fNC
				bw.Write((int)0);

				// DROPFILES.fWide
				bw.Write((int)1);

				Debug.Assert(ms.ToArray().Length == Marshal.SizeOf<DROPFILES>());

				// write filename
				path += "\0\0";
				int pathbytes = Encoding.Unicode.GetByteCount(path);
				bw.Write(Encoding.Unicode.GetBytes(path));

				// To HGLOBAL
				var arr = ms.ToArray();
				Debug.Assert(Marshal.SizeOf<DROPFILES>() + pathbytes == arr.Length);

				//hglobal = Marshal.AllocHGlobal(arr.Length);
				//Marshal.Copy(arr, 0, hglobal, arr.Length);

				hglobal = PInvoke.Kernel32.GlobalAlloc_IntPtr(PInvoke.Kernel32.GlobalAllocFlags.GMEM_MOVEABLE, new IntPtr(arr.Length));
				IntPtr hmem  = PInvoke.Kernel32.GlobalLock(hglobal);
				Marshal.Copy(arr, 0, hmem, arr.Length);
				PInvoke.Kernel32.GlobalUnlock(hmem);
			}

			short CF_HDROP = 15;
			FORMATETC ft = new FORMATETC
			{
				cfFormat = CF_HDROP,
				ptd = IntPtr.Zero,
				dwAspect = DVASPECT.DVASPECT_CONTENT,
				lindex = -1,
				tymed = TYMED.TYMED_HGLOBAL
			};
			STGMEDIUM sm = new STGMEDIUM
			{
				tymed = TYMED.TYMED_HGLOBAL,
				unionmember = hglobal,
			};

			KeyValuePair<FORMATETC, STGMEDIUM> addPair = new KeyValuePair<FORMATETC, STGMEDIUM>(ft, sm);
			_storage.Add(addPair);
		}

		private STGMEDIUM CopyMedium(ref STGMEDIUM medium)
		{
			STGMEDIUM sm = new STGMEDIUM();
			int hr = CopyStgMedium(ref medium, ref sm);
			if(hr != 0)
				throw Marshal.GetExceptionForHR(hr);
			return sm;
		}

		#region IDataObject
		public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
		{
			throw new NotImplementedException();
		}

		public void DUnadvise(int connection)
		{
			throw new NotImplementedException();
		}

		public int EnumDAdvise(out IEnumSTATDATA enumAdvise)
		{
			throw new NotImplementedException();
		}

		public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
		{
			if(DATADIR.DATADIR_GET == direction)
				return new EnumFORMATETC(_storage);
			throw new NotImplementedException("OLE_S_USEREG");
		}

		public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
		{
			throw new NotImplementedException();
		}

		public void GetData(ref FORMATETC format, out STGMEDIUM medium)
		{
			// Locate the data
			foreach(KeyValuePair<FORMATETC, STGMEDIUM> pair in _storage)
			{
				if((pair.Key.tymed & format.tymed) > 0
					&& pair.Key.dwAspect == format.dwAspect
					&& pair.Key.cfFormat == format.cfFormat)
				{
					// Found it. Return a copy of the data.
					medium = pair.Value;
					return;
				}
			}

			// Didn't find it. Return an empty data medium.
			medium = new STGMEDIUM();
		}

		public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
		{
			throw new NotImplementedException();
		}

		public int QueryGetData(ref FORMATETC format)
		{
			// We only support CONTENT aspect
			if((DVASPECT.DVASPECT_CONTENT & format.dwAspect) == 0)
				return DV_E_DVASPECT;

			int ret = DV_E_TYMED;
			// Try to locate the data
			// TODO: The ret, if not S_OK, is only relevant to the last item
			foreach(KeyValuePair<FORMATETC, STGMEDIUM> pair in _storage)
			{
				if((pair.Key.tymed & format.tymed) > 0)
				{
					if(pair.Key.cfFormat == format.cfFormat)
					{
						// Found it
						return S_OK;
					}
					else
					{
						// Found the medium type, but wrong format
						ret = DV_E_CLIPFORMAT;
					}
				}
				else
				{
					// Mismatch on medium type
					ret = DV_E_TYMED;
				}
			}

			return ret;
		}

		public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
		{
			Debug.WriteLine("SetData!!!!!!!!!!!!!!!!!!!");
			Console.WriteLine("SetData!!!!!!!!!!!!!!!!!!!!!");

			// If the format exists in our storage, remove it prior to resetting it
			foreach(KeyValuePair<FORMATETC, STGMEDIUM> pair in _storage)
			{
				if((pair.Key.tymed & formatIn.tymed) > 0
					&& pair.Key.dwAspect == formatIn.dwAspect
					&& pair.Key.cfFormat == formatIn.cfFormat)
				{
					_storage.Remove(pair);
					break;
				}
			}

			// If release is true, we'll take ownership of the medium.
			// If not, we'll make a copy of it.
			STGMEDIUM sm = medium;
			if(!release)
				sm = CopyMedium(ref medium);

			// Add it to the internal storage
			KeyValuePair<FORMATETC, STGMEDIUM> addPair =
				new KeyValuePair<FORMATETC, STGMEDIUM>(formatIn, sm);

			_storage.Add(addPair);
		}
		#endregion

		private class EnumFORMATETC : IEnumFORMATETC
		{
			private FORMATETC[] formats;
			private int currentIndex = 0;

			internal EnumFORMATETC(IList<KeyValuePair<FORMATETC, STGMEDIUM>> storage)
			{
				// Get the formats from the list
				formats = new FORMATETC[storage.Count];
				for(int i = 0; i < formats.Length; i++)
					formats[i] = storage[i].Key;
			}

			private EnumFORMATETC(FORMATETC[] formats)
			{
				// Get the formats as a copy of the array
				this.formats = new FORMATETC[formats.Length];
				formats.CopyTo(this.formats, 0);
			}

			public void Clone(out IEnumFORMATETC newEnum)
			{
				EnumFORMATETC ret = new EnumFORMATETC(formats);
				ret.currentIndex = currentIndex;
				newEnum = ret;
			}


			public int Reset()
			{
				currentIndex = 0;
				return 0; // S_OK
			}

			public int Skip(int celt)
			{
				if(currentIndex + celt > formats.Length)
					return 1; // S_FALSE
				currentIndex += celt;
				return 0; // S_OK
			}
			public int Next(int celt, FORMATETC[] rgelt, int[] pceltFetched)
			{
				// Start with zero fetched, in case we return early
				if(pceltFetched != null && pceltFetched.Length > 0)
					pceltFetched[0] = 0;

				// This will count down as we fetch elements
				int cReturn = celt;

				// Short circuit if they didn't request any elements, or didn't
				// provide room in the return array, or there are not more elements
				// to enumerate.

				if(celt <= 0 || rgelt == null || currentIndex >= formats.Length)
					return S_FALSE;

				// If the number of requested elements is not one, then we must
				// be able to tell the caller how many elements were fetched.
				if((pceltFetched == null || pceltFetched.Length < 1) && celt != 1)
					return S_FALSE;

				// If the number of elements in the return array is too small, we
				// throw. This is not a likely scenario, hence the exception.
				if(rgelt.Length < celt)
					throw new ArgumentException(
						"The number of elements in the return array is less than the "
						+ "number of elements requested");

				// Fetch the elements.
				for(int i = 0; currentIndex < formats.Length && cReturn > 0;
					i++, cReturn--, currentIndex++)
					rgelt[i] = formats[currentIndex];

				// Return the number of elements fetched
				if(pceltFetched != null && pceltFetched.Length > 0)
					pceltFetched[0] = celt - cReturn;

				// cReturn has the number of elements requested but not fetched.
				// It will be greater than zero, if multiple elements were requested
				// but we hit the end of the enumeration.
				return (cReturn == 0) ? S_OK : S_FALSE;
			}
		}

		public void ClearStorage()
		{
			foreach(KeyValuePair<FORMATETC, STGMEDIUM> pair in _storage)
			{
				STGMEDIUM medium = pair.Value;
				ReleaseStgMedium(ref medium);
			}

			_storage.Clear();
		}
	}
}
#endif