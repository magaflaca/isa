"""Adds a one-line <summary> to IsaClient classes that have none (module index / IntelliSense)."""
import glob
import os
import re

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src")
SUMMARIES = {
    "Bombs": "World Edit bomb filler: fills a selection with bombs (.bombs, .btype, .bamount).",
    "BrushId": "Ids of the world edit brush kinds.",
    "BrushOperations": "Applies the world edit brushes (tile, wall, erase, liquid, paint) to the world, locally and over the network.",
    "BrushState": "World edit brush settings and state: which brush is on, sizes, shape, pattern and the tiles marked for the next stroke.",
    "ColorMatching": "Color math for pixel art and map colors: closest block/wall color, hex parsing, wall color tables.",
    "ImageToSchematic": "Turns a picture into a TEdit-style schematic of blocks and walls.",
    "PasteEngine": "Pastes copied tiles and schematics into the world: tiles, walls, liquids, chests, tile objects, paint.",
    "PasteUndo": "Undo history of pastes (.undo / Ctrl+Z): the area under each paste is captured before it is changed.",
    "PixelArt": "Pixel art: draws images and player portraits into the world as blocks (.drawphoto, .plrart, .pixelart).",
    "Placement": "Placement tweaks: place anywhere, forced paint/coating, paint-only filters, protected walls, air blending.",
    "SchematicFiles": "Reads and writes TEdit schematic files (.TEditSch).",
    "Selection": "World edit selection: select, copy, fill, erase, convert and paste areas.",
    "TileItemLookup": "Tables between tile/wall ids and the items that place them, and tile comparison helpers.",
    "WorldEditBase": "Shared world edit helpers.",
    "ChatTools": "Chat tools: chat spammer, anti chat spam, enhanced chat box and keeping chat between worlds.",
    "PlayerListPanel": "Player List: the overlay and page of connected players, client badges (IsaClient / zMod), vitals and tracker.",
    "Spectate": "Spectating players from the Player List, optionally with their inventory in your HUD.",
    "ClientInfo": "Client name and version (Version + Hotfix) used by the updater, the title and the credits.",
    "ClientLifecycle": "Frame lifecycle of the client: initialization, the early and normal per-frame updates, entering a world.",
    "ClientLog": "Error log of the client (Terraria/IsaClient/Errors.txt).",
    "InfoAttribute": "Marks a member with a short description.",
    "MathUtil": "Small math helpers.",
    "Performance": "Frame time measurements and the low FPS guards used by brushes, pastes and anti-lag.",
    "QuickCheats": ".qc / .quickcheat: turns most cheats on at once.",
    "AutoClicker": "Auto clicker: holds or clicks the mouse buttons at a set speed, with optional hold/release cycles and a time limit.",
    "Combat": "Combat helpers: kill aura, butcher, crystal kill aura, projectile killing, NPC spawning and shimmering.",
    "ItemTools": "Item and buff commands (.item, .buff, .heal, .wear) and item tweaks (Zenith item, rope snakes, magical ice).",
    "NpcControl": "Controlling an NPC as if it were your player (.cnpc, single player).",
    "PlayerCheats": "Player cheats of the panel: god mode, infinite mana/ammo/breath, ranges, drops, buffs on death and more.",
    "PlayerTools": "Player tools: ghost mode and any item in any slot.",
    "RainbowText": "Writes text in the world with rainbow projectiles (.rainbow).",
    "RemoteMenus": "Opens storage and NPC menus from anywhere: piggy bank, safe, forge, void vault, dresser, stylist, guide, reforge.",
    "SelfCommands": "Commands about yourself: forced death, difficulty, invisibility, forced buff immunity, achievements.",
    "AiMenu": "Cheats → AI: the AI chat assistant settings.",
    "BiomeSpooferMenu": "Cheats → Biome Spoofers: zones forced on for your player.",
    "FileDrop": "Files dropped on the game window: pictures for pixel art and .plr characters.",
    "HeldItemMenu": "Cheats → Held Item and Item Mod Presets: every stat of the item in your hand, and presets of item mods.",
    "InfoMenu": "Cheats → Informational: overlays and notifications.",
    "InputState": "Keyboard and cursor state shared by the panel and the chat (open keys, Windows cursor).",
    "MenuControls": "Building blocks of panel rows: toggles, buttons, sliders, cycles, click sounds and tooltips.",
    "MenuListItem": "Panel row with a label and a value on the right.",
    "MenuSimpleItem": "Panel row that is a plain button.",
    "MenuSlider": "Panel row with a slider.",
    "MenuState": "State of the client's own user interfaces (panel open, hidden menu UI, mouse over).",
    "MenuToggle": "Panel row with an on/off toggle.",
    "MiscMenu": "Cheats → Miscellaneous: map, ground items, aura and butcher, free camera, chests, anti-lag.",
    "MusicMenu": "Cheats → Music: the music module and player.",
    "PlayerMenu": "Cheats → Player: player cheats and the auto clicker.",
    "PlayerPortraitElement": "Draws a player's portrait in a UI element.",
    "RenderMenu": "Cheats → Render: hitboxes, forced sights, shaders, zoom, brightness, chat and SSC options.",
    "TravelMenu": "Cheats → Travel: noclip, teleports, surfaces, flight and position updates.",
    "Wiki": "Shift+click wiki lookup of items, NPCs and tiles.",
    "WorldEditMenu": "Cheats → World Edit: packets, selection, paste, brushes, paint and world border.",
    "WorldInfoMenu": "World Information tab: world data, bosses, invasions, special seeds, utilities and flags.",
    "FreeCamera": "Free camera: moves the view without moving your player.",
    "Noclip": "Noclip: fly through blocks at the chosen speed.",
    "TeleportTools": "Teleports: to the mouse, from the map, to coordinates and to spawn, plus teleport request bypass.",
    "Travel": "Travel tweaks: surfaces (asphalt, bouncy, friction, conveyors), infinite flight, wing stats, portal physics.",
    "ClientSessionState": "Connection state of an extra client session.",
    "AudioTweaks": "Audio tweaks: forced soundtrack and the classic or disabled themes.",
    "MiniPlayer": "The small floating music player.",
    "MusicCommands": "Music chat commands (.play, .skip, .queue, ...).",
    "MusicDrawing": "Drawing helpers of the music windows.",
    "MusicLinks": "Turns what the user typed into something playable: links, Spotify/Apple/Deezer pages, searches.",
    "YtDlp": "Installs, updates and runs yt-dlp for the music player.",
    "NetDebug": "Netplay information on screen and debug names of packets.",
    "PacketPacing": "How fast world edit sends tile/erase/liquid/paint packets, and when it waits for the server.",
    "Rejoin": "Rejoining the current server (.rj) and auto-reconnect after a kick or lost connection.",
    "ServerDetection": "What the server is (TShock, server-side characters) and blocking server changes to your player.",
    "VanishDetection": "Finds players hidden by vanish on the server.",
    "AntiLag": "Anti-lag: blocks projectile/sound/particle spam, limits particles and dust, low FPS measures.",
    "BiomeSpoofers": "Applies the biome spoofers to the local player.",
    "Cosmetics": "Cosmetics: walking and death particles, death effects and sounds, Lucy messages, RGB map icons.",
    "Hitboxes": "Draws player, NPC, projectile and item hitboxes.",
    "InfoOverlays": "Extra information: IDs, item/NPC/tile data and the owner of dropped items.",
    "WorldView": "How the world is drawn: forced sights, shaders, invisible things, screen clamp and world border.",
    "Chests": "Chest tools: local chests, long names, locking/unlocking, filling, locked chests.",
    "MapTools": "Map: reveal, refresh, sections and export as an image.",
    "WorldCommands": "World tools: regenerate, scan/remove tiles, convert, ground items, actuation and plates.",
    "WorldInfo": "World information values and actions: bosses, invasions, orbs, moon phase, purity, size.",
}

added = 0
for path in glob.glob(os.path.join(SRC, "IsaClient.*", "*.cs")):
    raw = open(path, "rb").read()
    text = raw.decode("utf-8-sig")
    nl = "\r\n" if "\r\n" in text else "\n"
    for name, line in SUMMARIES.items():
        pattern = re.compile(r"(^|" + re.escape(nl) + r")((?:public|internal) (?:static |sealed |abstract )*(?:class|struct|enum) " + name + r"\b)")
        m = pattern.search(text)
        if not m:
            continue
        before = text[:m.start(2)]
        if before.rstrip().endswith("</summary>") or before.rstrip().endswith("]"):
            continue
        text = text[:m.start(2)] + "/// <summary>" + line + "</summary>" + nl + text[m.start(2):]
        added += 1
    open(path, "wb").write((b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b"") + text.encode("utf-8"))
print(f"{added} summaries added")
