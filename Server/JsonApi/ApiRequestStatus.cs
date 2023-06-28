using Shared;
using Shared.Packet.Packets;
using System.Dynamic;
using System.Net;
using System.Numerics;
using System.Text.Json.Serialization;

namespace Server.JsonApi;

using Mutators = Dictionary<string, Action<dynamic, Client>>;

public static class ApiRequestStatus {
    public static async Task<bool> Send(Context ctx) {
        StatusResponse resp = new StatusResponse {
            Settings = ApiRequestStatus.GetSettings(ctx),
            Players  = Player.GetPlayers(ctx),
        };
        await ctx.Send(resp);
        return true;
    }


    private static dynamic? GetSettings(Context ctx)
    {
        // output object
        dynamic settings = new ExpandoObject();

        // all permissions for Settings
        var allowedSettings = ctx.Permissions
            .Where(str => str.StartsWith("Status/Settings/"))
            .Select(str => str.Substring(16))
        ;

        var has_results = false;

        // copy all allowed Settings
        foreach (string allowedSetting in allowedSettings) {
            string lastKey = "";
            dynamic?  next = settings;
            dynamic  input = Settings.Instance;
            IDictionary<string, object> output = settings;

            // recursively go down the path
            foreach (string key in allowedSetting.Split("/")) {
                lastKey = key;

                if (next == null) { break; }
                output = (IDictionary<string, object>) next;

                // create the sublayer
                if (!output.ContainsKey(key)) { output.Add(key, new ExpandoObject()); }

                // traverse down the output object
                output.TryGetValue(key, out next);

                // traverse down the Settings object
                var prop = input.GetType().GetProperty(key);
                if (prop == null) {
                    JsonApi.Logger.Warn($"Property \"{allowedSetting}\" doesn't exist on the Settings object. This is probably a misconfiguration in the settings.json");
                    goto next;
                }
                input  = prop.GetValue(input, null);
            }

            if (lastKey != "") {
                // copy key with the actual value
                output.Remove(lastKey);
                output.Add(lastKey, input);
                has_results = true;
            }

            next:;
        }

        if (!has_results) { return null; }
        return settings;
    }


    private class StatusResponse {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public dynamic? Settings { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public dynamic[]? Players  { get; set; }
    }


    private static class Player {
        private static Mutators Mutators = new Mutators {
            ["Status/Players/ID"]       = (dynamic p, Client c) => p.ID       = c.Id,
            ["Status/Players/Name"]     = (dynamic p, Client c) => p.Name     = c.Name,
            ["Status/Players/Kingdom"]  = (dynamic p, Client c) => p.Kingdom  = Player.GetKingdom(c),
            ["Status/Players/Stage"]    = (dynamic p, Client c) => p.Stage    = Player.GetGamePacket(c)?.Stage ?? null,
            ["Status/Players/Scenario"] = (dynamic p, Client c) => p.Scenario = Player.GetGamePacket(c)?.ScenarioNum ?? null,
            ["Status/Players/Position"] = (dynamic p, Client c) => p.Position = Position.FromVector3(Player.GetPlayerPacket(c)?.Position ?? null),
            ["Status/Players/Rotation"] = (dynamic p, Client c) => p.Rotation = Rotation.FromQuaternion(Player.GetPlayerPacket(c)?.Rotation ?? null),
            ["Status/Players/Tagged"]   = (dynamic p, Client c) => p.Tagged   = Player.GetTagged(c),
            ["Status/Players/Costume"]  = (dynamic p, Client c) => p.Costume  = Costume.FromClient(c),
            ["Status/Players/Capture"]  = (dynamic p, Client c) => p.Capture  = Player.GetCapture(c),
            ["Status/Players/Is2D"]     = (dynamic p, Client c) => p.Is2D     = Player.GetGamePacket(c)?.Is2d ?? null,
            ["Status/Players/IPv4"]     = (dynamic p, Client c) => p.IPv4     = (c.Socket?.RemoteEndPoint as IPEndPoint)?.Address.ToString(),
        };


        public static dynamic[]? GetPlayers(Context ctx) {
            if (!ctx.HasPermission("Status/Players"))  { return null; }
            return ctx.server.ClientsConnected.Select((Client c) => Player.FromClient(ctx, c)).ToArray();
        }


        private static dynamic FromClient(Context ctx, Client c) {
            dynamic player  = new ExpandoObject();
            foreach (var (perm, mutate) in Mutators) {
                if (ctx.HasPermission(perm))  {
                    mutate(player, c);
                }
            }
            return player;
        }


        private static GamePacket? GetGamePacket(Client c) {
            object? lastGamePacket = null;
            c.Metadata.TryGetValue("lastGamePacket", out lastGamePacket);
            if (lastGamePacket == null) { return null; }
            return (GamePacket) lastGamePacket;
        }


        private static PlayerPacket? GetPlayerPacket(Client c) {
            object? lastPlayerPacket = null;
            c.Metadata.TryGetValue("lastPlayerPacket", out lastPlayerPacket);
            if (lastPlayerPacket == null) { return null; }
            return (PlayerPacket) lastPlayerPacket;
        }


        private static bool? GetTagged(Client c) {
            object? lastTagPacket = null;
            c.Metadata.TryGetValue("lastTagPacket", out lastTagPacket);
            if (lastTagPacket == null) { return null; }
            return ((TagPacket) lastTagPacket).IsIt;
        }


        private static string? GetCapture(Client c) {
            object? lastCapturePacket = null;
            c.Metadata.TryGetValue("lastCapturePacket", out lastCapturePacket);
            if (lastCapturePacket == null) { return null; }
            CapturePacket p = (CapturePacket) lastCapturePacket;
            if (p.ModelName == "") { return null; }
            return p.ModelName;
        }


        private static string? GetKingdom(Client c) {
            string? stage = Player.GetGamePacket(c)?.Stage ?? null;
            if (stage == null) { return null; }

            Stages.Stage2Alias.TryGetValue(stage, out string? alias);
            if (alias == null) { return null; }

            if (Stages.Alias2Kingdom.Contains(alias)) {
                return (string?) Stages.Alias2Kingdom[alias];
            }

            return null;
        }
    }


    private class Costume {
        public string Cap  { get; private set; }
        public string Body { get; private set; }


        private Costume(CostumePacket p) {
            this.Cap  = p.CapName;
            this.Body = p.BodyName;
        }


        public static Costume? FromClient(Client c) {
            if (c.CurrentCostume == null) { return null; }
            CostumePacket p = (CostumePacket) c.CurrentCostume!;
            return new Costume(p);
        }
    }


    private class Position {
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }


        private Position(float X, float Y, float Z) {
            this.X = X;
            this.Y = Y;
            this.Z = Z;
        }

        public static Position? FromVector3(Vector3? pos) {
            if (pos == null) { return null; }
            Vector3 p = (Vector3) pos;
            return new Position(p.X, p.Y, p.Z);
        }
    }


    private class Rotation {
        public float W { get; private set; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }

        private Rotation(float W, float X, float Y, float Z) {
            this.W = W;
            this.X = X;
            this.Y = Y;
            this.Z = Z;
        }

        public static Rotation? FromQuaternion(Quaternion? quat) {
            if (quat == null) { return null; }
            Quaternion q = (Quaternion) quat;
            return new Rotation(q.W, q.X, q.Y, q.Z);
        }
    }
}
