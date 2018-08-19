﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace NuklearDotNet {
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public unsafe delegate void FontStashAction(IntPtr Atlas);

	public static unsafe class NuklearAPI {
		public static nk_context* Ctx;
		static nk_allocator* Allocator;
		static nk_font_atlas* FontAtlas;
		static nk_draw_null_texture* NullTexture;
		static nk_convert_config* ConvertCfg;

		static nk_buffer* Commands, Vertices, Indices;
		static byte[] LastMemory;

		static nk_draw_vertex_layout_element* VertexLayout;
		static nk_plugin_alloc_t Alloc;
		static nk_plugin_free_t Free;

		static NuklearDevice Dev;
		static IFrameBuffered FrameBuffered;

		static bool ForceUpdateQueued;

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

		public delegate void HighLevelRenderHandler (nk_command *c);

		static IntPtr ManagedAlloc(IntPtr Size, bool ClearMem = true) {
			IntPtr Mem = Malloc(Size);

			if (ClearMem) 
				Memset(Mem, 0, Size);

			return Mem;
		}

		static IntPtr ManagedAlloc(int Size) {
			return ManagedAlloc(new IntPtr(Size));
		}

		static void ManagedFree(IntPtr Mem) {
			StdFree(Mem);
		}

		static void FontStash(FontStashAction A = null) {
			Nuklear.nk_font_atlas_init(FontAtlas, Allocator);
			Nuklear.nk_font_atlas_begin(FontAtlas);

			A?.Invoke(new IntPtr(FontAtlas));

			int W, H;
			IntPtr Image = Nuklear.nk_font_atlas_bake(FontAtlas, &W, &H, nk_font_atlas_format.NK_FONT_ATLAS_RGBA32);
			int TexHandle = Dev.CreateTextureHandle(W, H, Image);

			Nuklear.nk_font_atlas_end(FontAtlas, Nuklear.nk_handle_id(TexHandle), NullTexture);

			if (FontAtlas->default_font != null)
				Nuklear.nk_style_set_font(Ctx, &FontAtlas->default_font->handle);
		}


		static bool HandleInput() {
			bool HasInput = FrameBuffered == null || Dev.Events.Count > 0;

			if (HasInput) {
				Nuklear.nk_input_begin(Ctx);

				while (Dev.Events.Count > 0) {
					NuklearEvent E = Dev.Events.Dequeue();

					switch (E.EvtType) {
						case NuklearEvent.EventType.MouseButton:
							Nuklear.nk_input_button(Ctx, (nk_buttons)E.MButton, E.X, E.Y, E.Down ? 1 : 0);
							break;

						case NuklearEvent.EventType.MouseMove:
							Nuklear.nk_input_motion(Ctx, E.X, E.Y);
							break;

						case NuklearEvent.EventType.Scroll:
							Nuklear.nk_input_scroll(Ctx, new nk_vec2() { x = E.ScrollX, y = E.ScrollY });
							break;

						case NuklearEvent.EventType.Text:
							for (int i = 0; i < E.Text.Length; i++) {
								if (!char.IsControl(E.Text[i]))
									Nuklear.nk_input_unicode(Ctx, E.Text[i]);
							}

							break;

						case NuklearEvent.EventType.KeyboardKey:
							Nuklear.nk_input_key(Ctx, E.Key, E.Down ? 1 : 0);
							break;

						case NuklearEvent.EventType.ForceUpdate:
							break;

						default:
							throw new NotImplementedException();
					}
				}

				Nuklear.nk_input_end(Ctx);
			}

			return HasInput;
		}

		static void Render(bool HadInput, HighLevelRenderHandler handler) {
			if (HadInput) {
				bool Dirty = true;

				if (FrameBuffered != null) {
					Dirty = false;

					IntPtr MemoryBuffer = Nuklear.nk_buffer_memory(&Ctx->memory);
					if ((int)Ctx->memory.allocated == 0)
						Dirty = true;

					if (!Dirty) {
						if (LastMemory == null || LastMemory.Length < (int)Ctx->memory.allocated) {
							LastMemory = new byte[(int)Ctx->memory.allocated];
							Dirty = true;
						}
					}

					if (!Dirty) {
						fixed (byte* LastMemoryPtr = LastMemory)
							if (Memcmp(new IntPtr(LastMemoryPtr), MemoryBuffer, Ctx->memory.allocated) != 0) {
								Dirty = true;
								Marshal.Copy(MemoryBuffer, LastMemory, 0, (int)Ctx->memory.allocated);
							}
					}
				}

				if (Dirty) {
					if (handler != null) {
						Nuklear.nk_foreach(Ctx, (Cmd) => {
							handler(Cmd);
						});
					} else {
						NkConvertResult R = (NkConvertResult)Nuklear.nk_convert(Ctx, Commands, Vertices, Indices, ConvertCfg);
					    if (R != NkConvertResult.Success)
						    throw new Exception(R.ToString());

					    NkVertex[] NkVerts = new NkVertex[(int)Vertices->needed / sizeof(NkVertex)];
					    NkVertex* VertsPtr = (NkVertex*)Vertices->memory.ptr;

					    for (int i = 0; i < NkVerts.Length; i++) {
						    //NkVertex* V = &VertsPtr[i];
						    //NkVerts[i] = new NkVertex() { Position = new NkVector2f() { X = (int)V->Position.X, Y = (int)V->Position.Y }, Color = V->Color, UV = V->UV };

						    NkVerts[i] = VertsPtr[i];
					    }

					    ushort[] NkIndices = new ushort[(int)Indices->needed / sizeof(ushort)];
					    ushort* IndicesPtr = (ushort*)Indices->memory.ptr;
					    for (int i = 0; i < NkIndices.Length; i++)
						    NkIndices[i] = IndicesPtr[i];

					    Dev.SetBuffer(NkVerts, NkIndices);
					    FrameBuffered?.BeginBuffering();

					    uint Offset = 0;
					    Nuklear.nk_draw_foreach(Ctx, Commands, (Cmd) => {
						    if (Cmd->elem_count == 0)
							    return;

						    Dev.Render(Cmd->userdata, Cmd->texture.id, Cmd->clip_rect, Offset, Cmd->elem_count);
						    Offset += Cmd->elem_count;
					    });

					    FrameBuffered?.EndBuffering();
                    }
				}


				Nuklear.nk_clear(Ctx);
			}

			FrameBuffered?.RenderFinal();
		}

        public static nk_allocator* MakeAllocator () {
			var result = (nk_allocator*)ManagedAlloc(sizeof(nk_allocator));

			Alloc = (Handle, Old, Size) => ManagedAlloc(Size);
			Free = (Handle, Old) => ManagedFree(Old);

			result->alloc_nkpluginalloct = Marshal.GetFunctionPointerForDelegate(Alloc);
			result->free_nkpluginfreet = Marshal.GetFunctionPointerForDelegate(Free);

            return result;
        }

		//public  NuklearAPI(NuklearDevice Device) {
		public static void Init(NuklearDevice Device) {
			Dev = Device;
			FrameBuffered = Device as IFrameBuffered;

			// TODO: Free these later
			Ctx = (nk_context*)ManagedAlloc(sizeof(nk_context));
            Allocator = MakeAllocator();
			FontAtlas = (nk_font_atlas*)ManagedAlloc(sizeof(nk_font_atlas));
			NullTexture = (nk_draw_null_texture*)ManagedAlloc(sizeof(nk_draw_null_texture));
			ConvertCfg = (nk_convert_config*)ManagedAlloc(sizeof(nk_convert_config));
			Commands = (nk_buffer*)ManagedAlloc(sizeof(nk_buffer));
			Vertices = (nk_buffer*)ManagedAlloc(sizeof(nk_buffer));
			Indices = (nk_buffer*)ManagedAlloc(sizeof(nk_buffer));

			VertexLayout = (nk_draw_vertex_layout_element*)ManagedAlloc(sizeof(nk_draw_vertex_layout_element) * 4);
			VertexLayout[0] = new nk_draw_vertex_layout_element(nk_draw_vertex_layout_attribute.NK_VERTEX_POSITION, nk_draw_vertex_layout_format.NK_FORMAT_FLOAT,
				Marshal.OffsetOf(typeof(NkVertex), nameof(NkVertex.Position)));
			VertexLayout[1] = new nk_draw_vertex_layout_element(nk_draw_vertex_layout_attribute.NK_VERTEX_TEXCOORD, nk_draw_vertex_layout_format.NK_FORMAT_FLOAT,
				Marshal.OffsetOf(typeof(NkVertex), nameof(NkVertex.UV)));
			VertexLayout[2] = new nk_draw_vertex_layout_element(nk_draw_vertex_layout_attribute.NK_VERTEX_COLOR, nk_draw_vertex_layout_format.NK_FORMAT_R8G8B8A8,
				Marshal.OffsetOf(typeof(NkVertex), nameof(NkVertex.Color)));
			VertexLayout[3] = nk_draw_vertex_layout_element.NK_VERTEX_LAYOUT_END;

			Nuklear.nk_init(Ctx, Allocator, null);

			Dev.Init();
			FontStash(Dev.FontStash);

			ConvertCfg->shape_AA = nk_anti_aliasing.NK_ANTI_ALIASING_ON;
			ConvertCfg->line_AA = nk_anti_aliasing.NK_ANTI_ALIASING_ON;
			ConvertCfg->vertex_layout = VertexLayout;
			ConvertCfg->vertex_size = new IntPtr(sizeof(NkVertex));
			ConvertCfg->vertex_alignment = new IntPtr(1);
			ConvertCfg->circle_segment_count = 22;
			ConvertCfg->curve_segment_count = 22;
			ConvertCfg->arc_segment_count = 22;
			ConvertCfg->global_alpha = 1.0f;
			ConvertCfg->null_tex = *NullTexture;

			Nuklear.nk_buffer_init(Commands, Allocator, new IntPtr(4 * 1024));
			Nuklear.nk_buffer_init(Vertices, Allocator, new IntPtr(4 * 1024));
			Nuklear.nk_buffer_init(Indices, Allocator, new IntPtr(4 * 1024));
		}

        public static nk_user_font* AllocUserFont () {
            var result = (nk_user_font*)ManagedAlloc((IntPtr)sizeof(nk_user_font), true);
            return result;
        }

		public static void Frame(Action A, HighLevelRenderHandler B = null) {
			if (ForceUpdateQueued) {
				ForceUpdateQueued = false;

				Dev?.ForceUpdate();
			}

			bool HasInput;
			if (HasInput = HandleInput())
				A();
			Render(HasInput, B);
		}

		public static void SetDeltaTime(float Delta) {
			if (Ctx != null)
				Ctx->delta_time_Seconds = Delta;
		}

		public static bool Window(string Name, string Title, float X, float Y, float W, float H, NkPanelFlags Flags, Action A) {
			bool Res = true;

			if (Nuklear.nk_begin_titled(Ctx, Name, Title, new NkRect(X, Y, W, H), (uint)Flags) != 0)
				A?.Invoke();
			else
				Res = false;

			Nuklear.nk_end(Ctx);
			return Res;
		}

		public static bool Window(string Title, float X, float Y, float W, float H, NkPanelFlags Flags, Action A) => Window(Title, Title, X, Y, W, H, Flags, A);

		public static bool WindowIsClosed(string Name) => Nuklear.nk_window_is_closed(Ctx, Name) != 0;

		public static bool WindowIsHidden(string Name) => Nuklear.nk_window_is_hidden(Ctx, Name) != 0;

		public static bool WindowIsCollapsed(string Name) => Nuklear.nk_window_is_collapsed(Ctx, Name) != 0;

		public static bool Group(string Name, string Title, NkPanelFlags Flags, Action A) {
			bool Res = true;

			if (Nuklear.nk_group_begin_titled(Ctx, Name, Title, (uint)Flags) != 0)
				A?.Invoke();
			else
				Res = false;

			Nuklear.nk_group_end(Ctx);
			return Res;
		}

		public static bool Group(string Name, NkPanelFlags Flags, Action A) => Group(Name, Name, Flags, A);

		public static bool ButtonLabel(string Label) {
			return Nuklear.nk_button_label(Ctx, Label) != 0;
		}

		public static bool ButtonText(string Text) {
			return Nuklear.nk_button_text(Ctx, Text);
		}

		public static bool ButtonText(char Char) => ButtonText(Char.ToString());

		public static void LayoutRowStatic(float Height, int ItemWidth, int Cols) {
			Nuklear.nk_layout_row_static(Ctx, Height, ItemWidth, Cols);
		}

		public static void LayoutRowDynamic(float Height = 0, int Cols = 1) {
			Nuklear.nk_layout_row_dynamic(Ctx, Height, Cols);
		}

		public static void Label(string Txt, NkTextAlign TextAlign = (NkTextAlign)NkTextAlignment.NK_TEXT_LEFT) {
			Nuklear.nk_label(Ctx, Txt, (uint)TextAlign);
		}

		public static void LabelWrap(string Txt) {
			//Nuklear.nk_label(Ctx, Txt, (uint)TextAlign);
			Nuklear.nk_label_wrap(Ctx, Txt);
		}

		public static void LabelColored(string Txt, NkColor Clr, NkTextAlign TextAlign = (NkTextAlign)NkTextAlignment.NK_TEXT_LEFT) {
			Nuklear.nk_label_colored(Ctx, Txt, (uint)TextAlign, Clr);
		}

		public static void LabelColored(string Txt, byte R, byte G, byte B, byte A, NkTextAlign TextAlign = (NkTextAlign)NkTextAlignment.NK_TEXT_LEFT) {
			//Nuklear.nk_label_colored(Ctx, Txt, (uint)TextAlign, new NkColor() { r = R, g = G, b = B, a = A });
			LabelColored(Txt, new NkColor() { R = R, G = G, B = B, A = A }, TextAlign);
		}

		public static void LabelColoredWrap(string Txt, NkColor Clr) {
			Nuklear.nk_label_colored_wrap(Ctx, Txt, Clr);
		}

		public static void LabelColoredWrap(string Txt, byte R, byte G, byte B, byte A) {
			LabelColoredWrap(Txt, new NkColor() { R = R, G = G, B = B, A = A });
		}

		public static NkRect WindowGetBounds() {
			return Nuklear.nk_window_get_bounds(Ctx);
		}

		public static NkEditEvents EditString(NkEditTypes EditType, StringBuilder Buffer, nk_plugin_filter_t Filter) {
			return (NkEditEvents)Nuklear.nk_edit_string_zero_terminated(Ctx, (uint)EditType, Buffer, Buffer.MaxCapacity, Filter);
		}

		public static NkEditEvents EditString(NkEditTypes EditType, StringBuilder Buffer) {
			return EditString(EditType, Buffer, (ref nk_text_edit TextBox, uint Rune) => 1);
		}

		public static bool IsKeyPressed(NkKeys Key) {
			//Nuklear.nk_input_is_key_pressed()
			return Nuklear.nk_input_is_key_pressed(&Ctx->input, Key) != 0;
		}

		public static void QueueForceUpdate() {
			ForceUpdateQueued = true;
		}

		public static void WindowClose(string Name) {
			Nuklear.nk_window_close(Ctx, Name);
		}

		public static void SetClipboardCallback(Action<string> CopyFunc, Func<string> PasteFunc) {
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

			Ctx->clip.copyfun_nkPluginCopyT = Marshal.GetFunctionPointerForDelegate(NkCopyFunc);
			Ctx->clip.pastefun_nkPluginPasteT = Marshal.GetFunctionPointerForDelegate(NkPasteFunc);
		}
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct NkVector2f {
		public float X, Y;

		public override string ToString() {
			return string.Format("({0}, {1})", X, Y);
		}
	}

	/*[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct NkColor {
		public byte R, G, B, A;

		public override string ToString() {
			return string.Format("({0}, {1}, {2}, {3})", R, G, B, A);
		}
	}*/

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct NkVertex {
		public NkVector2f Position;
		public NkVector2f UV;
		public NkColor Color;

		public override string ToString() {
			return string.Format("Position: {0}; UV: {1}; Color: {2}", Position, UV, Color);
		}
	}

	public struct NuklearEvent {
		public enum EventType {
			MouseButton,
			MouseMove,
			Scroll,
			Text,
			KeyboardKey,
			ForceUpdate
		}

		public enum MouseButton {
			Left, Middle, Right
		}

		public EventType EvtType;
		public MouseButton MButton;
		public NkKeys Key;
		public int X, Y;
		public bool Down;
		public float ScrollX, ScrollY;
		public string Text;
	}

	public interface IFrameBuffered {
		void BeginBuffering();
		void EndBuffering();
		void RenderFinal();
	}

	public unsafe abstract class NuklearDevice {
		internal Queue<NuklearEvent> Events;

		public abstract void SetBuffer(NkVertex[] VertexBuffer, ushort[] IndexBuffer);
		public abstract void Render(NkHandle Userdata, int Texture, NkRect ClipRect, uint Offset, uint Count);
		public abstract int CreateTextureHandle(int W, int H, IntPtr Data);

		public NuklearDevice() {
			Events = new Queue<NuklearEvent>();
			ForceUpdate();
		}

		public virtual void Init() {
		}

		public virtual void FontStash(IntPtr Atlas) {
		}

		public void OnMouseButton(NuklearEvent.MouseButton MouseButton, int X, int Y, bool Down) {
			Events.Enqueue(new NuklearEvent() { EvtType = NuklearEvent.EventType.MouseButton, MButton = MouseButton, X = X, Y = Y, Down = Down });
		}

		public void OnMouseMove(int X, int Y) {
			Events.Enqueue(new NuklearEvent() { EvtType = NuklearEvent.EventType.MouseMove, X = X, Y = Y });
		}

		public void OnScroll(float ScrollX, float ScrollY) {
			Events.Enqueue(new NuklearEvent() { EvtType = NuklearEvent.EventType.Scroll, ScrollX = ScrollX, ScrollY = ScrollY });
		}

		public void OnText(string Txt) {
			Events.Enqueue(new NuklearEvent() { EvtType = NuklearEvent.EventType.Text, Text = Txt });
		}

		public void OnKey(NkKeys Key, bool Down) {
			Events.Enqueue(new NuklearEvent() { EvtType = NuklearEvent.EventType.KeyboardKey, Key = Key, Down = Down });
		}

		public void ForceUpdate() {
			Events.Enqueue(new NuklearEvent() { EvtType = NuklearEvent.EventType.ForceUpdate });
		}
	}

	public unsafe abstract class NuklearDeviceTex<T> : NuklearDevice {
		List<T> Textures;

		public NuklearDeviceTex() {
			Textures = new List<T>();
			Textures.Add(default(T)); // Start indices at 1
		}

		public sealed override int CreateTextureHandle(int W, int H, IntPtr Data) {
			T Tex = CreateTexture(W, H, Data);
			Textures.Add(Tex);
			return Textures.Count - 1;
		}

		public sealed override void Render(NkHandle Userdata, int Texture, NkRect ClipRect, uint Offset, uint Count) =>
			Render(Userdata, Textures[Texture], ClipRect, Offset, Count);

		public abstract T CreateTexture(int W, int H, IntPtr Data);
		public abstract void Render(NkHandle Userdata, T Texture, NkRect ClipRect, uint Offset, uint Count);
	}
}