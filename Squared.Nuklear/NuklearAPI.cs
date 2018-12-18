using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace NuklearDotNet {
    public static unsafe class NuklearAPI {
        static Nuklear.AssertHandler _OnNuklearAssert;

        static nk_allocator* Allocator;
        static nk_draw_null_texture* NullTexture;

        static nk_buffer* Commands;

        static nk_plugin_alloc_t Alloc;
        static nk_plugin_free_t Free;

        // TODO: Support swapping this, native memcmp is the fastest so it's used here
        [SuppressUnmanagedCodeSecurity]
        [DllImport("msvcrt", EntryPoint = "memcmp", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Memcmp(IntPtr A, IntPtr B, IntPtr Count);
        [SuppressUnmanagedCodeSecurity]
        [DllImport("msvcrt", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Memcpy(IntPtr A, IntPtr B, IntPtr Count);
        [SuppressUnmanagedCodeSecurity]
        [DllImport("msvcrt", EntryPoint = "memset", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Memset(IntPtr A, int Value, IntPtr Count);
        [SuppressUnmanagedCodeSecurity]
        [DllImport("msvcrt", EntryPoint = "malloc", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Malloc(IntPtr size);
        [SuppressUnmanagedCodeSecurity]
        [DllImport("msvcrt", EntryPoint = "free", CallingConvention = CallingConvention.Cdecl)]
        public static extern void StdFree(IntPtr P);

        [SuppressUnmanagedCodeSecurity]
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void HighLevelRenderHandler (nk_command *c);

        static IntPtr ManagedAlloc (IntPtr Size, bool ClearMem = true) {
            IntPtr Mem = Malloc(Size);

            if (ClearMem) 
                Memset(Mem, 0, Size);

            return Mem;
        }

        static IntPtr ManagedAlloc (int Size) {
            return ManagedAlloc(new IntPtr(Size));
        }

        static void ManagedFree (IntPtr Mem) {
            StdFree(Mem);
        }

        public static void Render (nk_context* ctx, HighLevelRenderHandler handler) {
            Nuklear.nk_foreach(ctx, (Cmd) => {
                handler(Cmd);
            });

            Nuklear.nk_clear(ctx);
        }

        public static nk_allocator* MakeAllocator () {
            var result = (nk_allocator*)ManagedAlloc(sizeof(nk_allocator));

            Alloc = (Handle, Old, Size) => ManagedAlloc(Size);
            Free = (Handle, Old) => ManagedFree(Old);

            result->alloc_nkpluginalloct = Marshal.GetFunctionPointerForDelegate(Alloc);
            result->free_nkpluginfreet = Marshal.GetFunctionPointerForDelegate(Free);

            return result;
        }

        private static void OnNuklearAssert (byte* file, int line, byte* expr) {
            var msg = "Assertion failed";
            if (file != null)
                msg += " at " + System.IO.Path.GetFileName(new string((sbyte*)file)) + ":" + line;
            if (expr != null)
                msg += ": " + new string((sbyte*)expr);
            throw new NuklearException(msg);
        }

        public static nk_context* Init () {
            _OnNuklearAssert = OnNuklearAssert;
            Nuklear.nk_set_assert_handler(_OnNuklearAssert);

            // TODO: Free these later
            var ctx = (nk_context*)ManagedAlloc(sizeof(nk_context));
            Allocator = MakeAllocator();
            NullTexture = (nk_draw_null_texture*)ManagedAlloc(sizeof(nk_draw_null_texture));
            Commands = (nk_buffer*)ManagedAlloc(sizeof(nk_buffer));

            Nuklear.nk_init(ctx, Allocator, null);

            Nuklear.nk_buffer_init(Commands, Allocator, new IntPtr(4 * 1024));

            return ctx;
        }

        public static nk_user_font* AllocUserFont () {
            var result = (nk_user_font*)ManagedAlloc((IntPtr)sizeof(nk_user_font), true);
            return result;
        }        

        public static void SetClipboardCallback(nk_context* ctx, Action<string> CopyFunc, Func<string> PasteFunc) {
            // TODO: Contains alloc and forget, don't call SetClipboardCallback too many times

            nk_plugin_copy_t NkCopyFunc = (Handle, Str, Len) => {
                byte[] Bytes = new byte[Len];

                for (int i = 0; i < Bytes.Length; i++)
                    Bytes[i] = Str[i];

                CopyFunc(Encoding.UTF8.GetString(Bytes));
            };

            nk_plugin_paste_t NkPasteFunc = (NkHandle Handle, ref nk_text_edit TextEdit) => {
                byte[] Bytes = Encoding.UTF8.GetBytes(PasteFunc());

                fixed (byte* BytesPtr = Bytes)
                fixed (nk_text_edit* TextEditPtr = &TextEdit)
                    Nuklear.nk_textedit_paste(TextEditPtr, BytesPtr, Bytes.Length);
            };

            GCHandle.Alloc(CopyFunc);
            GCHandle.Alloc(PasteFunc);
            GCHandle.Alloc(NkCopyFunc);
            GCHandle.Alloc(NkPasteFunc);

            ctx->clip.copyfun_nkPluginCopyT = Marshal.GetFunctionPointerForDelegate(NkCopyFunc);
            ctx->clip.pastefun_nkPluginPasteT = Marshal.GetFunctionPointerForDelegate(NkPasteFunc);
        }
    }

    public class NuklearException : Exception {
        public NuklearException (string msg)
            : base (msg) {
        }
    }
}
