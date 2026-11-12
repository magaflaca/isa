using System;
using System.Collections.Generic;
using IsaClient.Building;
using IsaClient.Core;
using IsaClient.Networking;
using IsaClient.Rendering;
using IsaClient.WorldTools;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.Initializers;
using Terraria.UI;

namespace IsaClient.Menu;

/// <summary>Cheats → World Edit: packets, selection, paste, brushes, paint and world border.</summary>
public static class WorldEditMenu
{
	/// <summary>The rows of this category, in display order.</summary>
	internal static readonly List<string> Entries = new List<string>
	{
		"Packet Mode", "Tile & Wall Packet Rate", "Erase Packet Rate", "Liquid Packet Rate", "Paint Packet Rate",
		"Bypass FPS & Timeout Protections", "Selection Mode", "Tile Packet Type", "Copy Selection",
		"Save Copied Tiles as Schematic", "Clear Copied Tiles", "Fill Selection", "Remove From Selection",
		"Delete Everything Selected", "Delete Selected Tiles", "Delete Selected Walls", "Remove Protected Walls",
		"Delete Selected Liquid", "Convert to Purity", "Convert to Corruption", "Convert to Hallow", "Convert to Crimson",
		"Convert Jungle to Mushrooms", "Convert to Desert", "Convert to Snow", "Convert to Forest",
		"Cancel Fill/Delete Action", "Clear Selection", "Paste Mode", "Auto-Exit Paste", "Load Tiles From Schematic",
		"Load Image (Draw Photo)", "Image Scale", "Blend Tiles", "Paste in TShock", "Cancel Paste", "Undo Last Paste",
		"Brush Shape", "Tile Brush", "Tile Brush Size", "Erase Brush", "Erase Brush Size", "Wall Brush", "Wall Brush Size",
		"Liquid Brush", "Liquid Brush Size", "Super Paint Brush", "Super Paint Brush Size", "Special Paint Pattern",
		"Special Paint Patterns 1-3 Use Deep Paint", "Brush Pattern", "World Border Size", "No Screen Clamp",
		"Persist Thin Vanilla Border", "Disable & Reset All"
	};

	private static MenuListItem _fillSelectionButton;

	private static MenuListItem _remFromSelectionButton;

	private static MenuSimpleItem _copySelectionButton;

	private static MenuSimpleItem _deleteSelectionButton;

	private static MenuSimpleItem _deleteSelectedTilesButton;

	private static MenuSimpleItem _deleteSelectedWallsButton;

	private static MenuSimpleItem _deleteSelectedLiquidButton;

	private static MenuSimpleItem _cancelFillOrDelActionButton;

	private static MenuSlider _packetRate;

	private static MenuSlider _erasePacketRate;

	private static MenuSlider _liquidPacketRate;

	private static MenuSlider _paintPacketRate;

	private static MenuSlider _tileBrushSize;

	private static MenuSlider _eraseBrushSize;

	private static MenuSlider _wallBrushSize;

	private static MenuSlider _liquidBrushSize;

	private static MenuSlider _superPaintBrushSize;

	private static MenuSlider _worldBorderSize;

	internal static UIElement Create(string cheat, Color color)
	{
		bool fakeBoolean = false;
		float fakeFloat = 0f;
		switch (cheat)
		{
		case "Bypass FPS & Timeout Protections":
			return MenuControls.MakeToggle(cheat, color, () => PacketPacing.BypassPacketProtections, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PacketPacing.BypassPacketProtections);
				ClientSettings.Save();
			});
		case "Remove Protected Walls":
			return MenuControls.MakeToggle(cheat, color, () => Placement.ForceProtectedWalls, delegate(UIMouseEvent evt)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Placement.ForceProtectedWalls);
				ClientSettings.Save();
			});
		case "Packet Mode":
		{
			UIElement uIElement = new MenuListItem(cheat, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref PacketPacing.TilePacketMaxMode, oneSound: true);
			};
			return uIElement;
		}
		case "Tile & Wall Packet Rate":
		{
			fakeFloat = (float)PacketPacing.TilePacketRate / 500f;
			UIElement uIElement = new MenuSlider(() => $"{cheat}: ({PacketPacing.TilePacketRate})", () => fakeFloat, delegate(float f)
			{
				KeyboardState state = Keyboard.GetState();
				fakeFloat = (state.IsKeyDown(Keys.LeftShift) ? ((float)Math.Round(f, 2)) : f);
				PacketPacing.TilePacketRate = (int)(500f * fakeFloat);
				if (PacketPacing.TilePacketRate <= 0)
				{
					PacketPacing.TilePacketRate = 1;
				}
			}, delegate
			{
				fakeFloat = UILinksInitializer.HandleSliderHorizontalInput(fakeFloat, 0f, 1f, 1f, 0.35f);
			}, 6903, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			_packetRate = (MenuSlider)uIElement;
			return uIElement;
		}
		case "Erase Packet Rate":
		{
			fakeFloat = (float)PacketPacing.ErasePacketRate / 500f;
			UIElement uIElement = new MenuSlider(() => $"{cheat}: ({PacketPacing.ErasePacketRate})", () => fakeFloat, delegate(float f)
			{
				KeyboardState state = Keyboard.GetState();
				fakeFloat = (state.IsKeyDown(Keys.LeftShift) ? ((float)Math.Round(f, 2)) : f);
				PacketPacing.ErasePacketRate = (int)(500f * fakeFloat);
				if (PacketPacing.ErasePacketRate <= 0)
				{
					PacketPacing.ErasePacketRate = 1;
				}
			}, delegate
			{
				fakeFloat = UILinksInitializer.HandleSliderHorizontalInput(fakeFloat, 0f, 1f, 1f, 0.35f);
			}, 6925, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			_erasePacketRate = (MenuSlider)uIElement;
			return uIElement;
		}
		case "Liquid Packet Rate":
		{
			fakeFloat = (float)PacketPacing.LiquidPacketRate / 500f;
			UIElement uIElement = new MenuSlider(() => $"{cheat}: ({PacketPacing.LiquidPacketRate})", () => fakeFloat, delegate(float f)
			{
				KeyboardState state = Keyboard.GetState();
				fakeFloat = (state.IsKeyDown(Keys.LeftShift) ? ((float)Math.Round(f, 2)) : f);
				PacketPacing.LiquidPacketRate = (int)(500f * fakeFloat);
				if (PacketPacing.LiquidPacketRate <= 0)
				{
					PacketPacing.LiquidPacketRate = 1;
				}
			}, delegate
			{
				fakeFloat = UILinksInitializer.HandleSliderHorizontalInput(fakeFloat, 0f, 1f, 1f, 0.35f);
			}, 6926, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			_liquidPacketRate = (MenuSlider)uIElement;
			return uIElement;
		}
		case "Paint Packet Rate":
		{
			fakeFloat = (float)PacketPacing.PaintPacketRate / 500f;
			UIElement uIElement = new MenuSlider(() => $"{cheat}: ({PacketPacing.PaintPacketRate})", () => fakeFloat, delegate(float f)
			{
				KeyboardState state = Keyboard.GetState();
				fakeFloat = (state.IsKeyDown(Keys.LeftShift) ? ((float)Math.Round(f, 2)) : f);
				PacketPacing.PaintPacketRate = (int)(500f * fakeFloat);
				if (PacketPacing.PaintPacketRate <= 0)
				{
					PacketPacing.PaintPacketRate = 1;
				}
			}, delegate
			{
				fakeFloat = UILinksInitializer.HandleSliderHorizontalInput(fakeFloat, 0f, 1f, 1f, 0.35f);
			}, 6927, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			_paintPacketRate = (MenuSlider)uIElement;
			return uIElement;
		}
		case "Selection Mode":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => Selection.SelectionMode, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Selection.SelectionMode);
			};
			return uIElement;
		}
		case "Tile Packet Type":
		{
			UIElement uIElement = new MenuListItem(cheat, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Selection.SelectionUseTileSync);
			};
			return uIElement;
		}
		case "Copy Selection":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode && Selection.SelectionA != Point.Zero && Selection.SelectionB != Point.Zero)
				{
					SoundEngine.PlaySound(11);
					Selection.CopyTilesFromFillOrDelMode();
				}
			};
			_copySelectionButton = (MenuSimpleItem)uIElement;
			return uIElement;
		}
		case "Save Copied Tiles as Schematic":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SaveSchematicEnabled);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SaveSchematicEnabled)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SaveSchematicEnabled)
				{
					SoundEngine.PlaySound(11);
					SchematicFiles.Save();
				}
			};
			return uIElement;
		}
		case "Clear Copied Tiles":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SaveSchematicEnabled);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SaveSchematicEnabled)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SaveSchematicEnabled)
				{
					SoundEngine.PlaySound(11);
					Selection.CopiedTiles.Clear();
				}
			};
			return uIElement;
		}
		case "Fill Selection":
		{
			UIElement uIElement = new MenuListItem(cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(11);
					if (Main.LocalPlayer.HeldItem.createTile >= 0 || Main.LocalPlayer.HeldItem.createWall > 0)
					{
						Selection.CancelAction = false;
						Selection.FillTilesSelectionModeV4(Main.LocalPlayer.HeldItem);
					}
					else
					{
						ClientChat.Info("You need to hold the block or wall you want to fill the selected area with.");
					}
				}
			};
			_fillSelectionButton = (MenuListItem)uIElement;
			return uIElement;
		}
		case "Remove From Selection":
		{
			UIElement uIElement = new MenuListItem(cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(11);
					if (Main.LocalPlayer.HeldItem.createTile >= 0)
					{
						Selection.CancelAction = false;
						WorldCommands.RemoveTilesOfType(Main.LocalPlayer.HeldItem.createTile);
					}
					else
					{
						ClientChat.Info("You need to hold the block you want to remove from the selected area.");
					}
				}
			};
			_remFromSelectionButton = (MenuListItem)uIElement;
			return uIElement;
		}
		case "Delete Everything Selected":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(11);
					Selection.CancelAction = false;
					Selection.EraseTileSelectionMode(0);
				}
			};
			_deleteSelectionButton = (MenuSimpleItem)uIElement;
			return uIElement;
		}
		case "Delete Selected Tiles":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(11);
					Selection.CancelAction = false;
					Selection.EraseTileSelectionMode(1);
				}
			};
			_deleteSelectedTilesButton = (MenuSimpleItem)uIElement;
			return uIElement;
		}
		case "Delete Selected Walls":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(11);
					Selection.CancelAction = false;
					Selection.EraseTileSelectionMode(2);
				}
			};
			_deleteSelectedWallsButton = (MenuSimpleItem)uIElement;
			return uIElement;
		}
		case "Delete Selected Liquid":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(11);
					Selection.CancelAction = false;
					Selection.EraseTileSelectionMode(3);
				}
			};
			_deleteSelectedLiquidButton = (MenuSimpleItem)uIElement;
			return uIElement;
		}
		case "Convert to Purity":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(11);
					WorldCommands.ConvertWorld(0, selection: true);
				}
			};
			return uIElement;
		}
		case "Convert to Corruption":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(11);
					WorldCommands.ConvertWorld(1, selection: true);
				}
			};
			return uIElement;
		}
		case "Convert to Hallow":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(11);
					WorldCommands.ConvertWorld(2, selection: true);
				}
			};
			return uIElement;
		}
		case "Convert to Crimson":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(11);
					WorldCommands.ConvertWorld(4, selection: true);
				}
			};
			return uIElement;
		}
		case "Convert Jungle to Mushrooms":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(11);
					WorldCommands.ConvertWorld(3, selection: true);
				}
			};
			return uIElement;
		}
		case "Convert to Desert":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(11);
					WorldCommands.ConvertWorld(5, selection: true);
				}
			};
			return uIElement;
		}
		case "Convert to Snow":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(11);
					WorldCommands.ConvertWorld(6, selection: true);
				}
			};
			return uIElement;
		}
		case "Convert to Forest":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.SelectionMode);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(11);
					WorldCommands.ConvertWorld(7, selection: true);
				}
			};
			return uIElement;
		}
		case "Cancel Fill/Delete Action":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.FillOrDelCancelEnabled);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.SelectionMode)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.FillOrDelCancelEnabled)
				{
					SoundEngine.PlaySound(11);
					Selection.CancelAction = true;
				}
			};
			_cancelFillOrDelActionButton = (MenuSimpleItem)uIElement;
			return uIElement;
		}
		case "Clear Selection":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				SoundEngine.PlaySound(11);
				Selection.SelectionA = Point.Zero;
				Selection.SelectionB = Point.Zero;
			};
			return uIElement;
		}
		case "Paste Mode":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => Selection.PasteMode, color, () => Selection.CopiedTiles.Count == 0);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.CopiedTiles.Count > 0)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				if (Selection.CopiedTiles.Count > 0)
				{
					MenuControls.OnToggleClicked(evt, cheat, ref Selection.PasteMode);
				}
			};
			return uIElement;
		}
		case "Auto-Exit Paste":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => Selection.AutoExitPaste, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Selection.AutoExitPaste);
			};
			return uIElement;
		}
		case "Load Image (Draw Photo)":
			return MenuControls.MakeButton(cheat, color, () => cheat + " (" + PixelArt.DrawPhotoScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%)", delegate
			{
				SoundEngine.PlaySound(11);
				PixelArt.DrawPhotoMode = false;
				PixelArt.LoadPhotosFromFile();
			});
		case "Image Scale":
		{
			float scaleFloat = PixelArt.DrawPhotoScale / 100f;
			UIElement scaleSlider = new MenuSlider(() => "Image Scale: (" + PixelArt.DrawPhotoScale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%)", () => PixelArt.DrawPhotoScale / 100f, delegate(float f)
			{
				scaleFloat = f;
				PixelArt.DrawPhotoScale = Math.Max(1f, (float)Math.Round(100f * f));
			}, delegate
			{
				scaleFloat = UILinksInitializer.HandleSliderHorizontalInput(scaleFloat, 0f, 1f, 1f, 0.35f);
				PixelArt.DrawPhotoScale = Math.Max(1f, (float)Math.Round(100f * scaleFloat));
			}, 6910, color);
			scaleSlider.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			scaleSlider.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			return scaleSlider;
		}
		case "Undo Last Paste":
			return MenuControls.MakeButton(cheat, color, () => cheat + " (" + PasteUndo.UndoCount + ")", delegate
			{
				SoundEngine.PlaySound(11);
				PasteUndo.UndoLastPaste();
			});
		case "Load Tiles From Schematic":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				SoundEngine.PlaySound(11);
				SchematicFiles.Load();
			};
			return uIElement;
		}
		case "Blend Tiles":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => Placement.BlendAirTileMode, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Placement.BlendAirTileMode);
			};
			return uIElement;
		}
		case "Paste in TShock":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => Selection.PasteInTShock, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Selection.PasteInTShock);
			};
			return uIElement;
		}
		case "Cancel Paste":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color, () => !Selection.PasteCancelEnabled);
			uIElement.OnMouseOver += delegate
			{
				if (Selection.PasteCancelEnabled)
				{
					SoundEngine.PlaySound(12);
				}
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				if (Selection.PasteCancelEnabled)
				{
					SoundEngine.PlaySound(11);
					Selection.CancelPaste = true;
				}
			};
			return uIElement;
		}
		case "Brush Shape":
		{
			UIElement uIElement = new MenuListItem(cheat, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref BrushState.BrushCircle, oneSound: true);
			};
			return uIElement;
		}
		case "Tile Brush":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => BrushState.TileBrush, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref BrushState.TileBrush);
			};
			return uIElement;
		}
		case "Tile Brush Size":
		{
			fakeFloat = (float)BrushState.TileBrushSize / 200f;
			UIElement uIElement = new MenuSlider(() => $"{cheat}: ({BrushState.TileBrushSize})", () => fakeFloat, delegate(float f)
			{
				KeyboardState state = Keyboard.GetState();
				fakeFloat = (state.IsKeyDown(Keys.LeftShift) ? ((float)Math.Round(f, 1)) : f);
				BrushState.TileBrushSize = (int)(200f * fakeFloat);
			}, delegate
			{
				fakeFloat = UILinksInitializer.HandleSliderHorizontalInput(fakeFloat, 0f, 1f, 1f, 0.35f);
			}, 6904, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			_tileBrushSize = (MenuSlider)uIElement;
			return uIElement;
		}
		case "Erase Brush":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => BrushState.EraseBrush, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref BrushState.EraseBrush);
			};
			return uIElement;
		}
		case "Erase Brush Size":
		{
			fakeFloat = (float)BrushState.EraseBrushSize / 200f;
			UIElement uIElement = new MenuSlider(() => $"{cheat}: ({BrushState.EraseBrushSize})", () => fakeFloat, delegate(float f)
			{
				KeyboardState state = Keyboard.GetState();
				fakeFloat = (state.IsKeyDown(Keys.LeftShift) ? ((float)Math.Round(f, 1)) : f);
				BrushState.EraseBrushSize = (int)(200f * fakeFloat);
			}, delegate
			{
				fakeFloat = UILinksInitializer.HandleSliderHorizontalInput(fakeFloat, 0f, 1f, 1f, 0.35f);
			}, 6905, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			_eraseBrushSize = (MenuSlider)uIElement;
			return uIElement;
		}
		case "Wall Brush":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => BrushState.WallBrush, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref BrushState.WallBrush);
			};
			return uIElement;
		}
		case "Wall Brush Size":
		{
			fakeFloat = (float)BrushState.WallBrushSize / 200f;
			UIElement uIElement = new MenuSlider(() => $"{cheat}: ({BrushState.WallBrushSize})", () => fakeFloat, delegate(float f)
			{
				KeyboardState state = Keyboard.GetState();
				fakeFloat = (state.IsKeyDown(Keys.LeftShift) ? ((float)Math.Round(f, 1)) : f);
				BrushState.WallBrushSize = (int)(200f * fakeFloat);
			}, delegate
			{
				fakeFloat = UILinksInitializer.HandleSliderHorizontalInput(fakeFloat, 0f, 1f, 1f, 0.35f);
			}, 6906, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			_wallBrushSize = (MenuSlider)uIElement;
			return uIElement;
		}
		case "Liquid Brush":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => BrushState.LiquidBrush, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref BrushState.LiquidBrush);
			};
			return uIElement;
		}
		case "Liquid Brush Size":
		{
			fakeFloat = (float)BrushState.LiquidBrushSize / 200f;
			UIElement uIElement = new MenuSlider(() => $"{cheat}: ({BrushState.LiquidBrushSize})", () => fakeFloat, delegate(float f)
			{
				KeyboardState state = Keyboard.GetState();
				fakeFloat = (state.IsKeyDown(Keys.LeftShift) ? ((float)Math.Round(f, 1)) : f);
				BrushState.LiquidBrushSize = (int)(200f * fakeFloat);
			}, delegate
			{
				fakeFloat = UILinksInitializer.HandleSliderHorizontalInput(fakeFloat, 0f, 1f, 1f, 0.35f);
			}, 6907, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			_liquidBrushSize = (MenuSlider)uIElement;
			return uIElement;
		}
		case "Super Paint Brush":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => BrushState.TilePaintBrush, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref BrushState.TilePaintBrush);
			};
			return uIElement;
		}
		case "Super Paint Brush Size":
		{
			fakeFloat = (float)BrushState.TilePaintBrushSize / 200f;
			UIElement uIElement = new MenuSlider(() => $"{cheat}: ({BrushState.TilePaintBrushSize})", () => fakeFloat, delegate(float f)
			{
				KeyboardState state = Keyboard.GetState();
				fakeFloat = (state.IsKeyDown(Keys.LeftShift) ? ((float)Math.Round(f, 1)) : f);
				BrushState.TilePaintBrushSize = (int)(200f * fakeFloat);
			}, delegate
			{
				fakeFloat = UILinksInitializer.HandleSliderHorizontalInput(fakeFloat, 0f, 1f, 1f, 0.35f);
			}, 6924, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			_superPaintBrushSize = (MenuSlider)uIElement;
			return uIElement;
		}
		case "Special Paint Pattern":
		{
			UIElement uIElement = new MenuListItem(cheat, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnCycleClicked(evt, cheat, 0, 16, ref Placement.RainbowPaintType);
			};
			uIElement.OnRightClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnCycleClicked(evt, cheat, 0, 16, ref Placement.RainbowPaintType, goBack: true);
			};
			return uIElement;
		}
		case "Special Paint Patterns 1-3 Use Deep Paint":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => Placement.RainbowPaintDeep, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref Placement.RainbowPaintDeep);
			};
			return uIElement;
		}
		case "Brush Pattern":
		{
			UIElement uIElement = new MenuListItem(cheat, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnCycleClicked(evt, cheat, 0, 40, ref BrushState.BrushPattern);
			};
			uIElement.OnRightClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnCycleClicked(evt, cheat, 0, 40, ref BrushState.BrushPattern, goBack: true);
			};
			return uIElement;
		}
		case "World Border Size":
		{
			fakeFloat = (float)(WorldView.WorldBorderSize + 2) / 42f;
			UIElement uIElement = new MenuSlider(() => cheat + ": (" + ((WorldView.WorldBorderSize > -2) ? (WorldView.WorldBorderSize + 1).ToString() : "Disabled") + ")", () => fakeFloat, delegate(float f)
			{
				fakeFloat = f;
				WorldView.WorldBorderSize = (int)(42f * fakeFloat) - 2;
			}, delegate
			{
				fakeFloat = UILinksInitializer.HandleSliderHorizontalInput(fakeFloat, 0f, 1f, 1f, 0.35f);
			}, 6908, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			_worldBorderSize = (MenuSlider)uIElement;
			return uIElement;
		}
		case "Persist Thin Vanilla Border":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => WorldView.KeepThinVanillaBorder, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.KeepThinVanillaBorder);
			};
			return uIElement;
		}
		case "No Screen Clamp":
		{
			UIElement uIElement = new MenuToggle(() => cheat, () => WorldView.NoScreenClamp, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate(UIMouseEvent evt, UIElement element)
			{
				MenuControls.OnToggleClicked(evt, cheat, ref WorldView.NoScreenClamp);
			};
			return uIElement;
		}
		case "Disable & Reset All":
		{
			UIElement uIElement = new MenuSimpleItem(() => cheat, color);
			uIElement.OnMouseOver += delegate
			{
				SoundEngine.PlaySound(12);
			};
			uIElement.OnUpdate += delegate(UIElement element)
			{
				MenuControls.ShowTooltip(element, cheat);
			};
			uIElement.OnLeftClick += delegate
			{
				DisableAll();
			};
			return uIElement;
		}
		default:
			return new MenuToggle(() => cheat, () => fakeBoolean, color);
		}
	}

	/// <summary>The category's "Disable All" button.</summary>
	internal static void DisableAll()
	{
		Selection.SelectionMode = false;
		Selection.CancelAction = true;
		Selection.CancelPaste = true;
		Selection.PasteMode = false;
		Placement.BlendAirTileMode = false;
		Selection.PasteInTShock = true;
		BrushState.TileBrush = false;
		BrushState.EraseBrush = false;
		BrushState.WallBrush = false;
		BrushState.LiquidBrush = false;
		BrushState.TilePaintBrush = false;
		Placement.RainbowPaintDeep = false;
		WorldView.WorldBorderSize = 40;
		WorldView.NoScreenClamp = false;
	}

	/// <summary>Hover text of a row of this category (null: none).</summary>
	internal static string Tooltip(string name)
	{
		return name switch
		{
			"Bypass FPS & Timeout Protections" => "Pasting, brushes and fills normally slow down to one packet per pause while the game runs\nunder ~50 FPS or the server has been silent for over a second (safer against kicks and disables).\nTurn this on to always follow your packet rates exactly.",
			"Remove Protected Walls" => "Selection erasing also removes dungeon walls (before Skeletron) and Lihzahrd walls (before Golem).\nSingle player: always works. Servers refuse to break them, so they are sent as the cleared tile:\nworks on vanilla servers, TShock usually puts them back. Blocks are not protected, only walls.",
			"Load Image (Draw Photo)" => "Load an image and turn it into tiles/walls ready to paste (same as .drawpic).\nYou can also drag an image onto the game window.",
			"Image Scale" => "Downscale images before converting them (e.g. 1000x1000 at 10% = 100x100).\nUse .picscale <percent> for decimals.",
			"Undo Last Paste" => "Restores what was there before the last paste (Ctrl+Z or .undo).\nStops a paste that is still running. Chest contents are not restored.",
			"Packet Mode" => "Change how packets should be sent.\nPacket Maximum sends up to the packet rate, then induces a 500ms delay.\nPacket 100/ms will send up until the number of packets sent/the packet rate = 0, then induce a 100ms delay.",
			"Tile & Wall Packet Rate" => "Change how fast packets should be sent for tile/wall placement.\nUsed by Paste for everything.\nHold LeftShift to adjust this by 5.",
			"Erase Packet Rate" => "Change how fast packets should be sent for tile/wall erasing.\nHold LeftShift to adjust this by 5.",
			"Liquid Packet Rate" => "Change how fast packets should be sent liquid modification.\nHold LeftShift to adjust this by 5.",
			"Paint Packet Rate" => "Change how fast packets should be sent for tile/wall painting.\nHold LeftShift to adjust this by 5.",
			"Selection Mode" => "Select an area using left & rigt click to preform actions on.",
			"Tile Packet Type" => "How selection mode's packets should be handled.\nUse Chunk for vanilla servers, and Singular for tShock servers.",
			"Copy Selection" => "Copy the currently selected tiles for use with paste mode, or to save as a schematic.",
			"Save Copied Tiles as Schematic" => "Save your currently copied tiles to a TEdit Schematic file.",
			"Clear Copied Tiles" => "Wipe your copied tiles.",
			"Fill Selection" => "Fill the current selection with the held block/wall.",
			"Remove From Selection" => "Remove the held tile from the current selection.",
			"Delete Everything Selected" => "Delete all tiles, walls, and liquids in the selection",
			"Delete Selected Tiles" => "Only remove the currently selected tiles.",
			"Delete Selected Walls" => "Only remove the currently selected walls.",
			"Delete Selected Liquid" => "Only remove the currently selected liquid.",
			"Convert to Purity" => "Converts the current selection to Purity.",
			"Convert to Corruption" => "Converts the current selection to Corruption.",
			"Convert to Hallow" => "Converts the current selection to Hallow.",
			"Convert to Crimson" => "Converts the current selection to Crimson.",
			"Convert Jungle to Mushrooms" => "Converts the Jungle within current selection to Mushrooms.",
			"Convert to Desert" => "Converts the current selection to Desert & purifies it.",
			"Convert to Snow" => "Converts the current selection to Snow & purifies it.",
			"Convert to Forest" => "Converts the current selection to Forest & purifies it.",
			"Cancel Fill/Delete Action" => "Cancels an ongoing fill/delete action.",
			"Clear Selection" => "Reset the selection to default.",
			"Paste Mode" => "Paste the currently copied tiles. If no tiles are copied,\nuse Selection Mode to copy a selection, or load a TEdit Schematic.",
			"Auto-Exit Paste" => "Exits paste mode automatically after starting a paste.",
			"Load Tiles From Schematic" => "Load tiles to paste from a TEdit Schematic",
			"Blend Tiles" => "By default, air tiles will remove existing blocks that overlap them when pasting.\nTurn this on to prevent the removal of existing blocks due to air.",
			"Paste in TShock" => "Turn this on to paste tiles on TShock servers.\n" + (ServerDetection.OnTshockServer ? IsaClient.Languages.L10n.T("You are on a TShock server.") : IsaClient.Languages.L10n.T("You are not on a TShock server.")) + "\nThis is imperfect and does not account for place styles.",
			"Cancel Paste" => "Cancels an ongoing paste action.",
			"Brush Shape" => "The shape of the brush modes.",
			"Tile Brush" => "Draw with tiles. Hold a tile to draw using it.",
			"Tile Brush Size" => "The size of the tile brush.\nHold LeftShift to adjust this by 20.",
			"Erase Brush" => "Erase tiles and walls. Hold anything but a tile/wall creating item to erase.",
			"Erase Brush Size" => "The size of the erase brush.\nHold LeftShift to adjust this by 20.",
			"Wall Brush" => "Draw with walls. Hold a wall to draw using it.",
			"Wall Brush Size" => "The size of the wall brush.\nHold LeftShift to adjust this by 20.",
			"Liquid Brush" => "Draw with liquid. Hold a liquid bucket to draw using it.",
			"Liquid Brush Size" => "The size of the liquid brush.\nHold LeftShift to adjust this by 20.",
			"Super Paint Brush" => "Draw over tiles or walls with paint. Hold a paint brush, paint roller, or paint scraper to draw over tiles or walls using it.",
			"Super Paint Brush Size" => "The size of the super paint brush.\nHold LeftShift to adjust this by 20.",
			"Special Paint Pattern" => "Paint with special patterns based on the position in the world.",
			"Special Paint Patterns 1-3 Use Deep Paint" => "Special Paint Patterns 1-3 will use deep paint. The others ignore this.",
			"World Border Size" => "The size of the world border, in tiles. Defaults to 41.\nAt -1, the world border is removed.",
			"No Screen Clamp" => "Don't clamp the screen within the world border.",
			"Persist Thin Vanilla Border" => "Keep a thin version of the vanilla world border in place despite the world border size.",
			"Brush Pattern" => "The pattern the brushes draw with. Left click to cycle forwards, right click to cycle backwards.",
			_ => null,
		};
	}

	/// <summary>Points the sliders at the current values; false while the category has not been built yet.</summary>
	internal static bool SyncSliders()
	{
		if (_packetRate == null || _worldBorderSize == null)
		{
			return false;
		}
		_packetRate.GetStatusFunction = () => (float)PacketPacing.TilePacketRate / 500f;
		_erasePacketRate.GetStatusFunction = () => (float)PacketPacing.ErasePacketRate / 500f;
		_liquidPacketRate.GetStatusFunction = () => (float)PacketPacing.LiquidPacketRate / 500f;
		_paintPacketRate.GetStatusFunction = () => (float)PacketPacing.PaintPacketRate / 500f;
		_tileBrushSize.GetStatusFunction = () => (float)BrushState.TileBrushSize / 200f;
		_eraseBrushSize.GetStatusFunction = () => (float)BrushState.EraseBrushSize / 200f;
		_wallBrushSize.GetStatusFunction = () => (float)BrushState.WallBrushSize / 200f;
		_liquidBrushSize.GetStatusFunction = () => (float)BrushState.LiquidBrushSize / 200f;
		_superPaintBrushSize.GetStatusFunction = () => (float)BrushState.TilePaintBrushSize / 200f;
		_worldBorderSize.GetStatusFunction = () => (float)(WorldView.WorldBorderSize + 2) / 42f;
		return true;
	}
}
