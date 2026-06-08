using MelonLoader;
using HarmonyLib;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Steamworks;
using System;
using System.Linq;
using System.Text;
using System.IO;
using Mono.Cecil;
using AsmResolver.Patching;

[assembly: MelonInfo(typeof(CWMultiplayer.MultiplayerManager), "Multiplayer Mod", "0.1.0", "Purely_K2")]
[assembly: MelonGame("Buried Things", "Cursed Words")]

namespace CWMultiplayer
{
    public class MultiplayerManager : MelonMod
    {
        #region Variables
        //Score and Encounter Stuff
        public static ScorePacket mostRecentScorePacket = new ScorePacket(0);
        public static ScorePacket currentRemainingTarget = new ScorePacket(0);
        public static EncounterController encounterController;
        public static bool inBoss = false;
        public static int myHighscore = 0;

        //Life and Multiplayer Stuff
        public static int health = 5;
        #endregion

        #region Melon Stuff
        public override void OnInitializeMelon()
        {
            CursedNetworking.SetUpNetworking();
            MelonLogger.Msg("Loaded Multiplayer Mod");
        }
        public override void OnApplicationQuit()
        {
            CursedNetworking.ShutDownNetwork();
            MelonLogger.Msg("Shut Down Multiplayer Mod");
        }
        #endregion

        #region Bosses Stuff
        [HarmonyPatch(typeof(EncounterController), "GenerateGrid", new System.Type[] {typeof(bool)})]
        public static class ApplyBossModifier_Patch //inBoss, boss modifiers, total target = 1
        {
            public static void Prefix(ref List<BossModifier> ____bossModifiers)
            {
                if(____bossModifiers.Count > 0) inBoss = true;
                if(ReceivedInfo.hasOpponent) ____bossModifiers = new List<BossModifier>();

                if(inBoss && ReceivedInfo.hasOpponent)
                {
                    encounterController.SetTotalTarget(1);
                }
            }
        }
        [HarmonyPatch(typeof(EncounterController), "Start")]
        public static class Encounter_Start_Patch //inBoss, boss modifiers, total target = 1
        {
            public static void Prefix(ref List<BossModifier> ____bossModifiers, ref EncounterController __instance)
            {
                encounterController = __instance;
                if(____bossModifiers.Count > 0)
                {
                    inBoss = true;
                }
                if(ReceivedInfo.hasOpponent) ____bossModifiers = new List<BossModifier>();

                if(inBoss && ReceivedInfo.hasOpponent)
                {
                    if(ReceivedInfo.opponentHighscore <= 0)
                        encounterController.SetTotalTarget(1);
                    else
                        encounterController.SetTotalTarget(ReceivedInfo.opponentHighscore);
                }
            }
        }
        [HarmonyPatch(typeof(EncounterController), "SubmitWord", new System.Type[] {typeof(List<TileSelection>), typeof(List<string>)})]
        public static class SubmitWord_Patch //SOMETHING CHANGED IN HERE FOR TESTING!
        {
            public static bool Prefix(ref int ____remainingGrids, ref EncounterController __instance, ref List<TileSelection> tiles, ref List<string> words)
            {
                if(____remainingGrids <= 0)
                {
                    inBoss = false;
                    if(ReceivedInfo.opponentHighscore == 0 && ReceivedInfo.opponentIsInBoss) //CHANGE TO ||
                    {
                        __instance.SubmitWord(tiles, words);
                        return false;
                    }
                    if(myHighscore > 0)
                    {
                        if(myHighscore > ReceivedInfo.opponentHighscore && ReceivedInfo.opponentHealth - 1 <= 0)
                        {
                            GameStatics.GetPlayer().CurrentRunProgress.SetStage(GameStatics.GetNumberOfStages());
                            GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType = NodeType.Boss;
                            GameStatics.GetPlayer().HasFacedUncursedBoss = true;
                        }
                    }
                }
                return true;
            }
            public static void Postfix(ref ScorePacket ____remainingTarget, ref int ____remainingGrids, ref EncounterController __instance)
            {
                if (inBoss && ____remainingGrids > 0)
                {
                    ____remainingTarget = new ScorePacket(mostRecentScorePacket.Score + ReceivedInfo.opponentHighscore);
                    if(mostRecentScorePacket.Score > myHighscore) myHighscore = (int)mostRecentScorePacket.Score;
                }
                else if(myHighscore > 0)
                {
                    if(ReceivedInfo.opponentHighscore > myHighscore)
                    {
                        health--;
                    }
                    myHighscore = 0;

                    if(health > 0) ____remainingTarget = new ScorePacket(mostRecentScorePacket.Score - 1);
                    else ____remainingTarget = new ScorePacket(mostRecentScorePacket.Score + 1);
                    MelonLogger.Msg("Ended Boss Round: " + health);
                }

                currentRemainingTarget = ____remainingTarget;
            }
        }
        #endregion

        #region Round Stuff
        //Get Score For Word
        [HarmonyPatch(typeof(ScoreCalculation), "GetScoreFromScoreCalcInfo", new System.Type[] { typeof(List<ScoreCalcVizInfo>) })]
        public static class GetScoreFromScoreCalcInfo_Patch
        {
            public static void Postfix(ref ScorePacket __result)
            {
                MelonLogger.Msg("Most Recent Score: " + __result);
                mostRecentScorePacket = __result;
            }
        }
        //Get Remaining Target (SubmitWord_Ptch moved into boss stuff)
        #endregion
    }
    public static class ReceivedInfo
    {
        public static bool hasOpponent = true;
        public static bool opponentIsInBoss = true;
        public static int receivedScore = 0;
        public static int opponentHighscore = 1;
        public static int opponentHealth = 5;
    }

    #region Networking Stuff
    public class CursedNetworking
    {
        #region Variables
        public static Socket myClient;
        public static TcpListener server;
        public static IPEndPoint serverEndPoint;
        public static IPAddress randIPAddress = IPAddress.Parse("000.000.000.000"); //Randomly Generated IP
        public static List<int> allActivePorts = new List<int>();
        public static int port;
        public static bool isHost = true;

        public struct PlayerPacket
        {
            public string playerName;
            public bool inBoss;
            public int highScore;
            public int health;
            public PlayerPacket(string name, int totHealth)
            {
                playerName = name;
                inBoss = false;
                highScore = 0;
                health = totHealth;
            }
            public void UpdatePacket(bool inBossFight, int hScore, int currHealth)
            {
                inBoss = inBossFight;
                highScore = hScore;
                health = currHealth;
            }
        }
        #endregion

        public static async Task SetUpNetworking()
        {
            PlayerPacket myPlayerPacket = new PlayerPacket("", MultiplayerManager.health);

            port = 54321;
            serverEndPoint = new IPEndPoint(IPAddress.IPv6Loopback, port);

                server = new TcpListener(IPAddress.IPv6Any, port);
                server.Server.DualMode = true;
                server.Start();
                // Start the server loop in the background so it doesn't block the client
                _ = Task.Run(() => StartServerLoopAsync());
            
            myClient = new(
                serverEndPoint.AddressFamily,
                SocketType.Stream,
                ProtocolType.Tcp
            );

            MelonLogger.Msg("Connectiong To Server...");
            await myClient.ConnectAsync(serverEndPoint);
            MelonLogger.Msg("Client Connected!");


            using NetworkStream stream = new NetworkStream(myClient, ownsSocket: false);
            using StreamWriter writer = new StreamWriter(stream) { AutoFlush = true };
            using StreamReader reader = new StreamReader(stream);

            while(true)
            {
                await Task.Delay(1000);

                if(myPlayerPacket.playerName == "")
                {
                    if(isHost)
                        myPlayerPacket.playerName = "Purely_K2";
                    else
                        myPlayerPacket.playerName = "Tech";
                }
                else
                {
                    await SendAndReceiveServerStuff(myPlayerPacket, writer, reader);
                }
            }
        }
        #region Server Stuff
        // Background server task to handle incoming connections
        private static async Task StartServerLoopAsync()
        {
            try
            {
                MelonLogger.Msg("Server loop started listening...");
                while (true)
                {
                    // Accept the incoming client connection asynchronously
                    Socket incomingClient = await server.AcceptSocketAsync();
                    _ = Task.Run(() => HandleIncomingClientAsync(incomingClient));
                }
            }
            catch (ObjectDisposedException) { /* Server stopped */ }
            catch (Exception ex) { MelonLogger.Msg($"Server error: {ex.Message}"); }
        }
        // Echoes back whatever text the client sends
        private static async Task HandleIncomingClientAsync(Socket clientSocket)
        {
            using NetworkStream stream = new NetworkStream(clientSocket, ownsSocket: true);
            using StreamReader reader = new StreamReader(stream);
            using StreamWriter writer = new StreamWriter(stream) { AutoFlush = true };

            try
            {
                while (true)
                {
                    string incomingMessage = await reader.ReadLineAsync();
                    if (incomingMessage == null) break; // Client disconnected

                    await writer.WriteLineAsync($"{incomingMessage} is connected");
                }
            }
            catch (Exception) { /* Handle disconnects cleanly */ }
        }
        #endregion
        public static async Task SendAndReceiveServerStuff(PlayerPacket playerPacket, StreamWriter writer, StreamReader reader)
        {
            if (myClient != null && myClient.Connected)
            {
                
                string messageToSend = playerPacket.playerName;
                await writer.WriteLineAsync(messageToSend);

                string response = await reader.ReadLineAsync();
                MelonLogger.Msg("Server Says: " + response);
            }
        }
        public static void ShutDownNetwork()
        {
            if (isHost && server != null) server.Stop();
            if (myClient != null && myClient.Connected)
            {
                myClient.Shutdown(SocketShutdown.Both);
                myClient.Close();
            }
        }
    }

    #endregion
}