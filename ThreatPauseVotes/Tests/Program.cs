using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Multiplayer.Client;
using Multiplayer.Client.AsyncTime;
using Multiplayer.Client.Comp;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using ThreatPauseVotes;
using Verse;
using MP = Multiplayer.Client.Multiplayer;

static class Program
{
    static int checks;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
    static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
    static void Seed(MultiplayerGameComp game)
    {
        foreach (var id in new[] { -1, 1, 2 })
            foreach (int player in new[] { 1, 2 }) game.playerData[player].SetTimeVote(id, TimeVote.Superfast);
    }
    static int Main()
    {
        Current.Game = Empty<Game>();
        var maps = new List<Map>();
        Set(Current.Game, "maps", maps);
        MP.game = Empty<MultiplayerGame>();
        var game = MP.game.gameComp = new MultiplayerGameComp { asyncTime = true, timeControl = TimeControl.LowestWins };
        MP.game.asyncWorldTimeComp = Empty<AsyncWorldTimeComp>();
        MP.game.asyncTimeComps = new List<AsyncTimeComp>();
        foreach (int id in new[] { 1, 2 })
        {
            var map = Empty<Map>(); map.uniqueID = id; maps.Add(map);
            var time = Empty<AsyncTimeComp>(); Set(time, "map", map);
            time.DesiredTimeSpeed = TimeSpeed.Superfast; MP.game.asyncTimeComps.Add(time);
        }
        MP.session = Empty<MultiplayerSession>(); MP.session.client = new DummyConnection();
        game.playerData = new Dictionary<int, PlayerData> { [1] = new(), [2] = new(), [3] = new() };
        Seed(game);
        AsyncTimeComp.executingCmdMap = maps[0];
        var pause = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.AsyncTime.ReceiveLetterPause"), "PauseOnLetter");
        pause.Invoke(null, new object[] { null });
        Check(game.playerData[1].GetTimeVoteOrNull(1) == null && game.playerData[2].GetTimeVoteOrNull(1) == null, "baseline auto-pause clears both votes");
        Seed(game);
        ThreatPauseVotesMod.ApplyPatches();
        pause.Invoke(null, new object[] { null });
        Check(game.playerData[1].GetTimeVoteOrNull(1) == TimeVote.Paused && game.playerData[2].GetTimeVoteOrNull(1) == TimeVote.Paused, "patched async auto-pause keeps both votes explicitly at zero");
        Check(MP.game.asyncTimeComps[0].DesiredTimeSpeed == TimeSpeed.Paused, "actual async map remains paused");
        Check(game.playerData[1].GetTimeVoteOrNull(2) == TimeVote.Superfast && game.playerData[1].GetTimeVoteOrNull(-1) == TimeVote.Superfast, "async pause leaves other map and world votes alone");
        game.playerData[1].SetTimeVote(1, TimeVote.Superfast);
        Check(game.GetLowestTimeVote(1) == TimeSpeed.Paused, "one player speeding up cannot override the other pause");
        game.playerData[2].SetTimeVote(1, TimeVote.Normal);
        Check(game.GetLowestTimeVote(1) == TimeSpeed.Normal, "time resumes at the slower speed after both players choose a speed");
        Check(game.playerData[3].GetTimeVoteOrNull(1) == null, "offline/abstaining record never acquires a blocking pause");
        game.asyncTime = false; Seed(game);
        pause.Invoke(null, new object[] { null });
        foreach (int id in new[] { -1, 1, 2 })
            Check(game.playerData[1].GetTimeVoteOrNull(id) == TimeVote.Paused && game.playerData[2].GetTimeVoteOrNull(id) == TimeVote.Paused, "sync auto-pause sets both votes to zero for tickable " + id);
        Check(MP.game.asyncTimeComps.TrueForAll(t => t.DesiredTimeSpeed == TimeSpeed.Paused), "sync auto-pause pauses all maps");
        game.playerData[1].SetTimeVote(-1, TimeVote.Superfast);
        Check(game.GetLowestTimeVote(-1) == TimeSpeed.Paused, "sync world stays paused until the second voter is ready");
        game.playerData[2].SetTimeVote(-1, TimeVote.PlayerResetGlobal);
        Check(game.GetLowestTimeVote(-1) == TimeSpeed.Superfast, "normal disconnect/desync reset still releases that player's votes");
        game.ResetAllTimeVotes(1);
        Check(game.playerData[1].GetTimeVoteOrNull(1) == null, "ordinary reset outside auto-pause still clears votes");
        Console.WriteLine("Threat Pause Votes: " + checks + " checks passed");
        return 0;
    }
    sealed class DummyConnection : ConnectionBase
    {
        protected override void SendRaw(byte[] data, bool reliable = true) { }
        protected override void OnClose(ServerDisconnectPacket? packet) { }
    }
}
