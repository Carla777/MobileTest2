using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

// http://blogs.msdn.com/andreww/archive/2008/11/24/using-managed-controls-as-activex-controls.aspx

namespace AICtrl
{
    class InteropUtils
    {
        public enum OLEMISC // Describes miscellaneous characteristics of an object or class of objects.
        {
            RECOMPOSEONRESIZE = 0x00000001,
            ONLYICONIC = 0x00000002,
            INSERTNOTREPLACE = 0x00000004,
            STATIC = 0x00000008,
            CANTLINKINSIDE = 0x00000010,
            CANLINKBYOLE1 = 0x00000020,
            ISLINKOBJECT = 0x00000040,
            INSIDEOUT = 0x00000080,
            ACTIVATEWHENVISIBLE = 0x00000100,
            RENDERINGISDEVICEINDEPENDENT = 0x00000200,
            INVISIBLEATRUNTIME = 0x00000400,
            ALWAYSRUN = 0x00000800,
            ACTSLIKEBUTTON = 0x00001000,
            ACTSLIKELABEL = 0x00002000,
            NOUIACTIVATE = 0x00004000,
            ALIGNABLE = 0x00008000,
            SIMPLEFRAME = 0x00010000,
            SETCLIENTSITEFIRST = 0x00020000,
            IMEMODE = 0x00040000,
            IGNOREACTIVATEWHENVISIBLE = 0x00080000,
            WANTSTOMENUMERGE = 0x00100000,
            SUPPORTSMULTILEVELUNDO = 0x00200000
        };

        public static void ComRegister(Type t)
        {
            ComRegister(t, OLEMISC.SETCLIENTSITEFIRST | OLEMISC.INSIDEOUT | OLEMISC.CANTLINKINSIDE | OLEMISC.RECOMPOSEONRESIZE);
        }

        public static void ComRegister(Type t, OLEMISC miscStatus)
        {
            string keyName = @"CLSID\" + t.GUID.ToString("B");

            using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(keyName, true))
            {
                key.CreateSubKey("Control").Close();

                using (RegistryKey subkey = key.CreateSubKey("MiscStatus"))
                {
                    subkey.SetValue("", (uint)miscStatus);
                }

                using (RegistryKey subkey = key.CreateSubKey("TypeLib"))
                {
                    Guid libid = Marshal.GetTypeLibGuidForAssembly(t.Assembly);
                    subkey.SetValue("", libid.ToString("B"));
                }

                using (RegistryKey subkey = key.CreateSubKey("Version"))
                {
                    Version ver = t.Assembly.GetName().Version;
                    string version = string.Format("{0}.{1}", ver.Major, ver.Minor);
                    subkey.SetValue("", version);
                }
            }
        }

        public static void ComUnregister(Type t)
        {
            try
            {
                // Delete the entire CLSID\{clsid} subtree for this component.
                string keyName = @"CLSID\" + t.GUID.ToString("B");
                Registry.ClassesRoot.DeleteSubKeyTree(keyName);
            }
            catch (ArgumentException)
            {
                // Ignore exception thrown if key doesn't exist
            }
        }
    }
}
