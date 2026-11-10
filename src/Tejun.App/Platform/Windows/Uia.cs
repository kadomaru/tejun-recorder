using System.Runtime.InteropServices;

namespace Tejun.App.Platform.Windows;

/// <summary>
/// UI オートメーションで、指定した場所にある部品（ボタンなど）の名前と種類を調べる。
/// COM のインターフェースを必要な分だけ、関数表の番号で直接呼ぶ。
/// </summary>
internal static unsafe class Uia
{
    static readonly Guid ClsidAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");
    static readonly Guid IidAutomation = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");

    static readonly object Gate = new();
    static IntPtr _automation;
    static bool _failed;

    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr instance);

    // IUIAutomation::ElementFromPoint
    const int ElementFromPointSlot = 7;
    // IUIAutomationElement::get_CurrentControlType / get_CurrentName
    const int ControlTypeSlot = 21, NameSlot = 23;

    public static (string Name, string Type) FromPoint(int x, int y)
    {
        try
        {
            lock (Gate)
            {
                if (_failed) return ("", "");
                if (_automation == IntPtr.Zero)
                {
                    const uint CLSCTX_INPROC_SERVER = 1;
                    if (CoCreateInstance(ClsidAutomation, IntPtr.Zero, CLSCTX_INPROC_SERVER, IidAutomation, out _automation) != 0
                        || _automation == IntPtr.Zero)
                    {
                        _failed = true;
                        return ("", "");
                    }
                }
            }

            var vtable = *(void***)_automation;
            IntPtr element = IntPtr.Zero;
            var pt = new POINT { X = x, Y = y };
            var hr = ((delegate* unmanaged[Stdcall]<IntPtr, POINT, IntPtr*, int>)vtable[ElementFromPointSlot])(_automation, pt, &element);
            if (hr != 0 || element == IntPtr.Zero) return ("", "");

            try
            {
                var ev = *(void***)element;
                var controlType = 0;
                ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)ev[ControlTypeSlot])(element, &controlType);
                IntPtr bstr = IntPtr.Zero;
                ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)ev[NameSlot])(element, &bstr);
                var name = "";
                if (bstr != IntPtr.Zero)
                {
                    name = Marshal.PtrToStringBSTR(bstr) ?? "";
                    Marshal.FreeBSTR(bstr);
                }
                // 文書や枠そのものの名前は、押した部品の名前としては使えない
                if (controlType is 50030 or 50033 or 50032 or 50025) name = "";
                return (name, TypeName(controlType));
            }
            finally
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(void***)element)[2])(element); // Release
            }
        }
        catch (Exception)
        {
            return ("", "");
        }
    }

    static string TypeName(int controlType) => controlType switch
    {
        50000 or 50031 => "ボタン",
        50002 => "チェックボックス",
        50003 => "の選択欄",
        50004 => "の入力欄",
        50005 => "リンク",
        50011 => "メニュー",
        50013 => "ラジオボタン",
        50019 => "タブ",
        _ => "",
    };
}
