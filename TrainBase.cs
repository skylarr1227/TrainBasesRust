using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;

using Oxide.Core;              // Interface.Oxide.DataFileSystem
using Oxide.Game.Rust.Cui;

namespace Carbon.Plugins
{
    [Info("Train Base Anchors", "Skylarr + ChatGPT", "1.3.0")]
    [Description("Attach invisible, buildable foundation anchors to train cars via command/UI, no Find(...) API dependency.")]
    public class TrainBase : CarbonPlugin
    {
        private const string PERM_USE = "trainbase.use";
        private const string PanelName = "trainbase.ui";

        private Configuration _config;
        private StoredData _data;

        // In-memory indices to avoid serverEntities.Find(...)
        private readonly Dictionary<uint, BaseNetworkable> _byId = new Dictionary<uint, BaseNetworkable>();
        private readonly Dictionary<uint, TrainCar> _anchorToTrain = new Dictionary<uint, TrainCar>();

        // ──────────────────────────────────────────────────────────────────────────
        // Wagon kinds (detected from prefab name)
        // ──────────────────────────────────────────────────────────────────────────
        private enum CarKind { Unknown, Workcart, Flatbed, Ore, Covered, Engine }

        // ──────────────────────────────────────────────────────────────────────────
        // Config
        // ──────────────────────────────────────────────────────────────────────────
        private class Configuration
        {
            [JsonProperty("Anchor Prefab (foundation)")]
            public string AnchorPrefab = "assets/prefabs/building core/foundation/foundation.prefab";

            [JsonProperty("Raycast Distance To TrainCar")]
            public float RaycastDistance = 8f;

            [JsonProperty("UI Colors")]
            public UIColors Ui = new UIColors();

            [JsonProperty("Presets (per wagon kind)")]
            public Dictionary<string, List<Vector3>> Presets = new Dictionary<string, List<Vector3>>
            {
                ["workcart"] = new List<Vector3> { new Vector3(-1.20f, 0.05f, 0f), new Vector3(0f, 0.05f, 0f), new Vector3(1.20f, 0.05f, 0f) },
                ["flatbed"]  = new List<Vector3> { new Vector3(-1.60f, 0.05f, 0f), new Vector3(0f, 0.05f, 0f), new Vector3(1.60f, 0.05f, 0f) },
                ["ore"]      = new List<Vector3> { new Vector3(-1.30f, 0.05f, 0f), new Vector3(0f, 0.05f, 0f), new Vector3(1.30f, 0.05f, 0f) },
                ["covered"]  = new List<Vector3> { new Vector3(-1.35f, 0.05f, 0f), new Vector3(0f, 0.05f, 0f), new Vector3(1.35f, 0.05f, 0f) },
                ["engine"]   = new List<Vector3> { new Vector3(-1.10f, 0.05f, 0f), new Vector3(0f, 0.05f, 0f), new Vector3(1.10f, 0.05f, 0f) },
                ["unknown"]  = new List<Vector3> { new Vector3(-1.40f, 0.05f, 0f), new Vector3(0f, 0.05f, 0f), new Vector3(1.40f, 0.05f, 0f) },
            };
        }

        private class UIColors
        {
            public string Panel = "0.07 0.07 0.09 0.96";
            public string Title = "0.2 0.8 1.0 0.9";
            public string Button = "0.18 0.18 0.22 0.9";
            public string Danger = "0.6 0.15 0.15 0.95";
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Data
        // ──────────────────────────────────────────────────────────────────────────
        private class StoredData
        {
            // Store train ids and anchor ids as uint for consistency
            public Dictionary<uint, TrainRecord> Trains = new Dictionary<uint, TrainRecord>();
        }

        private class TrainRecord
        {
            public uint TrainNetId;
            public List<uint> AnchorIds = new List<uint>();
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ──────────────────────────────────────────────────────────────────────────
        private void Init()
        {
            permission.RegisterPermission(PERM_USE, this);
            LoadConfigValues();
            LoadData();

            // Build index from currently spawned entities
            IndexAllEntities();
            // Re-link any saved anchors to live entities & purge dead
            SweepAnchors();
        }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList)
                CuiHelper.DestroyUi(p, PanelName);
        }

        private void OnServerSave() => SaveData();

        // Keep our index live
        private void OnEntitySpawned(BaseNetworkable ent)
        {
            if (ent == null || ent.net == null) return;
            var id = SafeId(ent);
            if (id != 0) _byId[id] = ent;
        }

        private void OnEntityKill(BaseNetworkable ent)
        {
            if (ent == null || ent.net == null) return;
            var id = SafeId(ent);
            _byId.Remove(id);

            if (ent is TrainCar train)
            {
                var key = SafeId(ent);
                if (_data.Trains.TryGetValue(key, out var rec))
                {
                    foreach (var aid in rec.AnchorIds.ToList())
                    {
                        var e = GetEntity(aid) as BaseEntity;
                        if (e != null && !e.IsDestroyed) e.Kill();
                        _anchorToTrain.Remove(aid);
                    }
                    _data.Trains.Remove(key);
                    SaveData();
                }
                return;
            }

            if (ent is BuildingBlock bb)
            {
                var bbId = SafeId(bb);
                if (_anchorToTrain.TryGetValue(bbId, out _))
                {
                    _anchorToTrain.Remove(bbId);
                    foreach (var kv in _data.Trains.ToList())
                    {
                        if (kv.Value.AnchorIds.Remove(bbId))
                        {
                            if (kv.Value.AnchorIds.Count == 0)
                                _data.Trains.Remove(kv.Key);
                            SaveData();
                            break;
                        }
                    }
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Commands
        // ──────────────────────────────────────────────────────────────────────────
        [ChatCommand("trainbase")]
        private void CmdTrainBase(BasePlayer player, string cmd, string[] args)
        {
            if (!(player != null && (player.IsAdmin || permission.UserHasPermission(player.UserIDString, PERM_USE))))
            {
                Reply(player, "You don't have permission.");
                return;
            }

            if (args.Length >= 1)
            {
                var sub = args[0].ToLower();

                if (sub == "clear")
                {
                    if (TryGetLookTrain(player, out var train))
                    {
                        ClearAnchors(train);
                        Reply(player, "Cleared anchors on this train car.");
                    }
                    else Reply(player, "Look at a train car within range.");
                    return;
                }

                if (sub == "nudge")
                {
                    if (args.Length == 4
                        && float.TryParse(args[1], out var x)
                        && float.TryParse(args[2], out var y)
                        && float.TryParse(args[3], out var z))
                    {
                        if (TryGetLookTrain(player, out var train))
                        {
                            var a = CreateAnchor(train, new Vector3(x, y, z), Quaternion.identity);
                            Reply(player, a != null ? $"Added anchor at local offset {x} {y} {z}." : "Failed to create anchor.");
                        }
                        else Reply(player, "Look at a train car within range.");
                        return;
                    }
                    Reply(player, "Usage: /trainbase nudge <x> <y> <z>");
                    return;
                }

                if (sub == "preset")
                {
                    if (!TryGetLookTrain(player, out var train))
                    {
                        Reply(player, "Look at a train car within range.");
                        return;
                    }

                    CarKind kind;
                    if (args.Length >= 2)
                    {
                        var arg = args[1].ToLower();
                        kind = arg == "auto" ? DetectKind(train) : ParseKind(arg);
                    }
                    else kind = DetectKind(train);

                    var applied = ApplyPresetAnchors(train, kind, clearFirst: true);
                    Reply(player, applied ? $"Applied preset: {kind}" : "No preset anchors applied.");
                    return;
                }
            }

            if (!TryGetLookTrain(player, out _))
            {
                Reply(player, "Look at a train car within range.");
                return;
            }

            OpenUI(player);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Core
        // ──────────────────────────────────────────────────────────────────────────
        private bool TryGetLookTrain(BasePlayer player, out TrainCar train)
        {
            train = null;
            var eyes = player.eyes?.position ?? player.transform.position + Vector3.up * 1.6f;
            var dir = (player.eyes?.BodyForward() ?? player.transform.forward);

            if (Physics.Raycast(eyes, dir, out var hit, _config.RaycastDistance))
            {
                var ent = hit.GetEntity();
                if (ent == null) return false;
                train = ent.GetComponentInParent<TrainCar>();
                return train != null;
            }
            return false;
        }

        private BuildingBlock CreateAnchor(TrainCar train, Vector3 localPos, Quaternion localRot)
        {
            var ent = GameManager.server.CreateEntity(
                _config.AnchorPrefab,
                train.transform.TransformPoint(localPos),
                train.transform.rotation * localRot,
                true);

            if (ent == null) return null;

            var bb = ent as BuildingBlock;
            if (bb == null)
            {
                ent.Kill();
                PrintError("Configured prefab is not a BuildingBlock.");
                return null;
            }

            bb.grade = BuildingGrade.Enum.Metal;
            bb.health = bb.MaxHealth();
            ent.enableSaving = true;
            ent.Spawn();

            ent.SetParent(train, worldPositionStays: true);
            HideRenderers(ent);

            var tid = SafeId(train);
            if (!_data.Trains.TryGetValue(tid, out var rec))
            {
                rec = new TrainRecord { TrainNetId = tid };
                _data.Trains[tid] = rec;
            }

            var id = SafeId(bb);
            rec.AnchorIds.Add(id);
            _anchorToTrain[id] = train;
            _byId[id] = bb; // index the new anchor
            SaveData();

            bb.SendNetworkUpdateImmediate();
            return bb;
        }

        private void ClearAnchors(TrainCar train)
        {
            var tid = SafeId(train);
            if (_data.Trains.TryGetValue(tid, out var rec))
            {
                foreach (var aid in rec.AnchorIds.ToList())
                {
                    var e = GetEntity(aid) as BaseEntity;
                    if (e != null && !e.IsDestroyed) e.Kill();
                    _anchorToTrain.Remove(aid);
                }
                _data.Trains.Remove(tid);
                SaveData();
            }
        }

        private void SweepAnchors()
        {
            foreach (var kv in _data.Trains.ToList())
            {
                var train = GetEntity(kv.Key) as TrainCar;
                if (train == null || train.IsDestroyed)
                {
                    _data.Trains.Remove(kv.Key);
                    continue;
                }

                for (int i = kv.Value.AnchorIds.Count - 1; i >= 0; i--)
                {
                    var eid = kv.Value.AnchorIds[i];
                    var e = GetEntity(eid) as BaseEntity;
                    if (e == null || e.IsDestroyed)
                        kv.Value.AnchorIds.RemoveAt(i);
                    else
                        _anchorToTrain[SafeId(e)] = train;
                }
            }
            SaveData();
        }

        private void HideRenderers(BaseEntity entity)
        {
            foreach (var r in entity.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
            foreach (var l in entity.GetComponentsInChildren<LODGroup>(true))
                l.gameObject.SetActive(false);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Wagon detection & presets
        // ──────────────────────────────────────────────────────────────────────────
        private CarKind DetectKind(TrainCar train)
        {
            var name = (train?.ShortPrefabName ?? train?.PrefabName ?? string.Empty).ToLower();

            if (name.Contains("workcart")) return CarKind.Workcart;
            if (name.Contains("flatbed") || name.Contains("platform")) return CarKind.Flatbed;
            if (name.Contains("ore") || name.Contains("hopper")) return CarKind.Ore;
            if (name.Contains("covered") || name.Contains("box")) return CarKind.Covered;
            if (name.Contains("engine") || name.Contains("locomotive")) return CarKind.Engine;

            return CarKind.Unknown;
        }

        private CarKind ParseKind(string s)
        {
            switch (s)
            {
                case "workcart": return CarKind.Workcart;
                case "flatbed":  return CarKind.Flatbed;
                case "ore":      return CarKind.Ore;
                case "covered":  return CarKind.Covered;
                case "engine":   return CarKind.Engine;
                default:         return CarKind.Unknown;
            }
        }

        private bool ApplyPresetAnchors(TrainCar train, CarKind kind, bool clearFirst)
        {
            if (train == null) return false;

            var key = KindKey(kind);
            if (!_config.Presets.TryGetValue(key, out var list) || list == null || list.Count == 0)
            {
                if (!_config.Presets.TryGetValue("unknown", out list) || list == null || list.Count == 0)
                    return false;
            }

            if (clearFirst) ClearAnchors(train);

            var madeAny = false;
            foreach (var v in list)
                madeAny |= CreateAnchor(train, v, Quaternion.identity) != null;

            return madeAny;
        }

        private string KindKey(CarKind k)
        {
            switch (k)
            {
                case CarKind.Workcart: return "workcart";
                case CarKind.Flatbed:  return "flatbed";
                case CarKind.Ore:      return "ore";
                case CarKind.Covered:  return "covered";
                case CarKind.Engine:   return "engine";
                default:               return "unknown";
            }
        }

        // ──────────────────────────────────────────────────────────────────────────
        // UI
        // ──────────────────────────────────────────────────────────────────────────
        private void OpenUI(BasePlayer player)
        {
            CloseUI(player);

            var c = _config.Ui;
            var ui = new CuiElementContainer();

            var panel = ui.Add(new CuiPanel
            {
                Image = { Color = c.Panel },
                RectTransform = { AnchorMin = "0.35 0.3", AnchorMax = "0.65 0.75" },
                CursorEnabled = true
            }, "Overlay", PanelName);

            ui.Add(new CuiLabel
            {
                Text = { Text = "Train Base Anchors", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = c.Title },
                RectTransform = { AnchorMin = "0.05 0.86", AnchorMax = "0.95 0.98" }
            }, panel);

            AddBtn(ui, panel, "Apply Preset (Auto)", "0.06 0.72 0.94 0.82", c.Button, "chat.say \"/trainbase preset auto\";");
            AddBtn(ui, panel, "Workcart",           "0.06 0.60 0.30 0.70",  c.Button, "chat.say \"/trainbase preset workcart\";");
            AddBtn(ui, panel, "Flatbed",            "0.34 0.60 0.58 0.70",  c.Button, "chat.say \"/trainbase preset flatbed\";");
            AddBtn(ui, panel, "Ore Hopper",         "0.62 0.60 0.94 0.70",  c.Button, "chat.say \"/trainbase preset ore\";");
            AddBtn(ui, panel, "Covered/Box",        "0.06 0.48 0.30 0.58",  c.Button, "chat.say \"/trainbase preset covered\";");
            AddBtn(ui, panel, "Engine/Locomotive",  "0.34 0.48 0.58 0.58",  c.Button, "chat.say \"/trainbase preset engine\";");

            AddBtn(ui, panel, "Add Center",         "0.06 0.34 0.94 0.44",  c.Button, "chat.say \"/trainbase nudge 0 0.05 0\";");
            AddBtn(ui, panel, "Add Left (-X)",      "0.06 0.24 0.48 0.34",  c.Button, "chat.say \"/trainbase nudge -1.4 0.05 0\";");
            AddBtn(ui, panel, "Add Right (+X)",     "0.52 0.24 0.94 0.34",  c.Button, "chat.say \"/trainbase nudge 1.4 0.05 0\";");

            AddBtn(ui, panel, "Clear Anchors",      "0.06 0.12 0.94 0.22",  c.Danger, "chat.say \"/trainbase clear\";");
            AddBtn(ui, panel, "Close",              "0.40 0.03 0.60 0.09",  c.Button, $"ui.destroy {PanelName}");

            CuiHelper.AddUi(player, ui);
        }

        private void AddBtn(CuiElementContainer c, string parent, string text, string minmax, string color, string command)
        {
            var p = minmax.Split(' ');
            c.Add(new CuiButton
            {
                Button = { Color = color, Command = command },
                RectTransform = { AnchorMin = $"{p[0]} {p[1]}", AnchorMax = $"{p[2]} {p[3]}" },
                Text = { Text = text, Align = TextAnchor.MiddleCenter, FontSize = 14 }
            }, parent);
        }

        private void CloseUI(BasePlayer player) => CuiHelper.DestroyUi(player, PanelName);

        // ──────────────────────────────────────────────────────────────────────────
        // Indexing & Utilities (no Find calls)
        // ──────────────────────────────────────────────────────────────────────────
        private void IndexAllEntities()
        {
            foreach (var ent in BaseNetworkable.serverEntities)
            {
                if (ent == null || ent.net == null) continue;
                _byId[SafeId(ent)] = ent;
            }
        }

        private BaseNetworkable GetEntity(uint id)
        {
            if (id == 0) return null;
            if (_byId.TryGetValue(id, out var bn) && bn != null && !bn.IsDestroyed)
                return bn;

            // Fallback scan (rare)
            foreach (var e in BaseNetworkable.serverEntities)
            {
                if (e == null || e.net == null) continue;
                if (SafeId(e) == id && !e.IsDestroyed)
                {
                    _byId[id] = e;
                    return e;
                }
            }
            return null;
        }

        private static uint SafeId(BaseNetworkable e)
        {
            try
            {
                // net.ID.Value can be uint or ulong depending on build; coerce to uint
                return e?.net != null ? Convert.ToUInt32(e.net.ID.Value) : 0u;
            }
            catch { return 0u; }
        }

        private void LoadConfigValues()
        {
            _config = Config.ReadObject<Configuration>() ?? new Configuration();
            SaveConfig();
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();

        private void LoadData() =>
            _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData();

        private void SaveData() =>
            Interface.Oxide.DataFileSystem.WriteObject(Name, _data);

        private void Reply(BasePlayer p, string msg) =>
            PrintToChat(p, $"<color=#9ee7ff>[TrainBase]</color> {msg}");
    }
}
