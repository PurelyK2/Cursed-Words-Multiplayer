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
        public static Socket myClient;
        public static TcpListener server;
        public static IPEndPoint iPEndPoint;
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

        public static async Task SetUpNetworking()
        {
            PlayerPacket myPlayerPacket = new PlayerPacket("", MultiplayerManager.health);

            port = 2026;
            if (isHost)
            {
                server = new TcpListener(IPAddress.Loopback, port);
                server.Start();
            }
            iPEndPoint = new IPEndPoint(IPAddress.Loopback, port);

            using Socket client = new(
                iPEndPoint.AddressFamily,
                SocketType.Stream,
                ProtocolType.Tcp
            );
            myClient = client;
            await myClient.ConnectAsync(iPEndPoint);
            
            MelonLogger.Msg("Client Connected!");
            
            while(myClient.Connected)
            {
                await Task.Delay(1000);
                if(myPlayerPacket.playerName == "")
                {
                    if(SteamManager.Initialized)
                    {
                        string steamName = SteamFriends.GetPersonaName();
                        myPlayerPacket.playerName = steamName;
                        MelonLogger.Msg(steamName);
                    }
                    else
                    {
                        myPlayerPacket.playerName = "Unnamed";
                    }
                }
                else
                {
                    SendVariablesToServer(myPlayerPacket);
                    TakeVariablesFromServer();
                }
                MelonLogger.Msg("Updated");
            }
        }
        public static async void SendVariablesToServer(PlayerPacket playerPacket)
        {
            if(isHost)
            {
            }
            if(playerPacket.playerName == "")
            {
                //add packet to server packets list (If needed)
            }
            byte[] nameBytes = new byte[1024];
            myClient.SendTo(nameBytes, myClient.RemoteEndPoint);
            MelonLogger.Msg("Name Sent");
        }
            
        public static async void TakeVariablesFromServer()
        {
            if (isHost)
            {
            }
            try
            {
                byte[] nameBytes = new byte[1024];
                MelonLogger.Msg(Encoding.UTF8.GetString(nameBytes));
            }
            catch(System.Exception e)
            {
                MelonLogger.Msg(e);
            }
        }
        public static void ShutDownNetwork()
        {
            if(isHost) server.Stop();
            myClient.Shutdown(SocketShutdown.Both);
        }
    }

    #endregion
}