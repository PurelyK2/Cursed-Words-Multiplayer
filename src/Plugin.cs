using MelonLoader;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Steamworks;
using System.Linq;
using UnityEngine.InputSystem.UI;
using System.Threading.Tasks;

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
        public static EncounterSummaryDisplayController encounterSummaryDisplayController;
        #endregion

        #region Melon Stuff
        public override void OnInitializeMelon()
        {
            CursedNetworking.SetUpNetworking();
            MelonLogger.Msg("Loaded Multiplayer Mod");
        }
        public override void OnApplicationQuit()
        {
            CursedUI.DisableCallbacks();
            if(CursedUI.lobbyName != "")
            {
                SteamMatchmaking.LeaveLobby(CursedUI.lobbyID);
                CursedUI.lobbyName = "";
            }
            MelonLogger.Msg("Shut Down Multiplayer Mod");
        }
        #endregion

        #region Bosses Stuff
        [HarmonyPatch(typeof(EncounterController), "GenerateGrid", new System.Type[] {typeof(bool)})]
        public static class ApplyBossModifier_Patch //inBoss, boss modifiers, total target = 1
        {
            public static void Prefix(ref List<BossModifier> ____bossModifiers)
            {
                if(____bossModifiers.Count > 0)
                {
                    CursedNetworking.myPlayerPacket.UpdatePacket(true, CursedNetworking.myPlayerPacket.highScore, CursedNetworking.myPlayerPacket.health);
                }
                if(ReceivedInfo.hasOpponent) ____bossModifiers = new List<BossModifier>();

                if(CursedNetworking.myPlayerPacket.inBoss && ReceivedInfo.hasOpponent)
                {
                    if(ReceivedInfo.opponentHighscore > 0)
                        encounterController.SetTotalTarget(ReceivedInfo.opponentHighscore);
                    else
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
                    CursedNetworking.myPlayerPacket.inBoss = true;
                }
                if(ReceivedInfo.hasOpponent) ____bossModifiers = new List<BossModifier>();

                if(CursedNetworking.myPlayerPacket.inBoss && ReceivedInfo.hasOpponent)
                {
                    if(ReceivedInfo.opponentHighscore <= 0)
                        encounterController.SetTotalTarget(1);
                    else
                        encounterController.SetTotalTarget(ReceivedInfo.opponentHighscore);
                }
            }
        }
        [HarmonyPatch(typeof(EncounterController), "SubmitWord", new System.Type[] { typeof(List<TileSelection>), typeof(List<string>) })]
        public static class SubmitWord_Patch
        {
            private static async Task AsyncronousWaiting(EncounterController encounterController, List<TileSelection> tiles, List<string> words)
            {
                await Task.Delay(100);
                encounterController.SubmitWord(tiles, words);
            }
            public static bool Prefix(ref int ____remainingGrids, ref EncounterController __instance, ref List<TileSelection> tiles, ref List<string> words, ref GridData ____gridData, ref List<HistoricWord> ____previousWords)
            {
                if(____remainingGrids <= 0 && ReceivedInfo.hasOpponent)
                {
                    if(CursedNetworking.myPlayerPacket.inBoss)
                    {
                        MelonLogger.Msg("Finished Battle!");
                        //Get Final Score To Test For High Score
                        List<Item> itemsList = new List<Item>();
                        {
                            List<Item> list = new List<Item>();
                            foreach (TileSelection tileSelection in tiles)
                            {
                                Tile selectedTile = tileSelection.SelectedTile;
                                if (selectedTile.GetGlyphType() == GlyphType.ScatteredItem)
                                {
                                    list.Add(selectedTile.ScatteredItem);
                                }
                            }
                            list.AddRange(GameStatics.GetPlayer().GetAllItems(false));

                            itemsList = list;
                        }
                        List<ScoreCalcVizInfo> steps = ScoreCalculation.CalculateOverallScore(tiles, words, itemsList, ____previousWords, new List<BossModifier>(), ____gridData, encounterController.CurrentGridsGenerated());
                        ScorePacket scorePacket = ScoreCalculation.GetScoreFromScoreCalcInfo(steps);
                        if((int)scorePacket.Score > CursedNetworking.myPlayerPacket.highScore)
                        {
                            CursedNetworking.myPlayerPacket.UpdatePacket(false, (int)scorePacket.Score, CursedNetworking.myPlayerPacket.health);
                        }
                        else
                        {
                            CursedNetworking.myPlayerPacket.UpdatePacket(false, CursedNetworking.myPlayerPacket.highScore, CursedNetworking.myPlayerPacket.health);
                        }
                    }
                    //Freezes Game Until Opponent Gets There (If was in boss)
                    if((ReceivedInfo.opponentHighscore == 0 || ReceivedInfo.opponentIsInBoss) && CursedNetworking.myPlayerPacket.highScore > 0) //haven't gotten to or are in boss (in boss has high score, out of it doesn't)
                    {
                        MelonLogger.Msg("Waiting");
                        _ = AsyncronousWaiting(__instance, tiles, words);
                        return false;
                    }
                    else
                    {
                        MelonLogger.Msg("Opponent Is Done, continuing...");
                    }
                    if(CursedNetworking.myPlayerPacket.highScore > 0)
                    {
                        if(CursedNetworking.myPlayerPacket.highScore > ReceivedInfo.opponentHighscore && ReceivedInfo.opponentHealth - 1 <= 0)
                        {
                            GameStatics.GetPlayer().CurrentRunProgress.SetStage(GameStatics.GetNumberOfStages());
                            GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType = NodeType.Boss;
                            GameStatics.GetPlayer().HasFacedUncursedBoss = true;
                        }
                    }
                    MelonLogger.Msg("Deciding Who Won Between " + CursedNetworking.myPlayerPacket.highScore + " and " + ReceivedInfo.opponentHighscore);
                }
                return true;
            }
            public static void Postfix(ref ScorePacket ____remainingTarget, ref int ____remainingGrids, ref EncounterController __instance)
            {
                if (CursedNetworking.myPlayerPacket.inBoss && ____remainingGrids > 0 && ReceivedInfo.hasOpponent)
                {
                    if(ReceivedInfo.opponentHighscore > 0)
                        ____remainingTarget = new ScorePacket(mostRecentScorePacket.Score + ReceivedInfo.opponentHighscore);
                    else ____remainingTarget = new ScorePacket(mostRecentScorePacket.Score + 1);
                    if(mostRecentScorePacket.Score > CursedNetworking.myPlayerPacket.highScore) CursedNetworking.myPlayerPacket.UpdatePacket(true, (int)mostRecentScorePacket.Score, CursedNetworking.myPlayerPacket.health);
                    else MelonLogger.Msg("Not Highest Score");
                }
                else if(CursedNetworking.myPlayerPacket.highScore > 0 && ReceivedInfo.opponentHighscore > 0 && !ReceivedInfo.opponentIsInBoss)
                {
                    if(ReceivedInfo.opponentHighscore > CursedNetworking.myPlayerPacket.highScore)
                    {
                        CursedNetworking.myPlayerPacket.UpdatePacket(CursedNetworking.myPlayerPacket.inBoss, CursedNetworking.myPlayerPacket.highScore, CursedNetworking.myPlayerPacket.health - 1);
                        MelonLogger.Msg("You Lost A Life!\nCurrent Life: " + CursedNetworking.myPlayerPacket.health);
                    }
                    else
                    {
                        MelonLogger.Msg("You Won The Floor!");
                    }

                    if(CursedNetworking.myPlayerPacket.health > 0) ____remainingTarget = new ScorePacket(mostRecentScorePacket.Score - 1);
                    else ____remainingTarget = new ScorePacket(mostRecentScorePacket.Score + 1);
                    
                    CursedNetworking.myPlayerPacket.UpdatePacket(CursedNetworking.myPlayerPacket.inBoss, 0, CursedNetworking.myPlayerPacket.health);
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
                mostRecentScorePacket = __result;
            }
        }
        //Get Remaining Target (SubmitWord_Patch moved into boss stuff)
        #endregion
        
        #region Other Stuff
        private static Vector2 resolution = new Vector2(Screen.width, Screen.height);
        public override void OnUpdate()
        {
            base.OnUpdate();
            if(SteamAPI.Init()) SteamAPI.RunCallbacks();
            if(CursedUI.lobbyID != CSteamID.Nil) CursedNetworking.UpdateAndSendPlayerPacket();
            if(SteamMatchmaking.GetNumLobbyMembers(CursedUI.lobbyID) > 1 && !ReceivedInfo.hasOpponent)
            {
                ReceivedInfo.hasOpponent = true;
                MelonLogger.Msg("2 People In Lobby!");
            }

            //Resolution Stuff
            if(resolution.x != Screen.width || resolution.y != Screen.height)
            {
                resolution.x = Screen.width;
                resolution.y = Screen.height;
                CursedUI.SetUpUIAppearance();
            }

            //Waiting For Opponent Stuff
            if(ReceivedInfo.hasOpponent)
            {
                CursedUI.waitingTextObj.SetActive(CursedNetworking.myPlayerPacket.highScore > 0 && !CursedNetworking.myPlayerPacket.inBoss);
            }
        }
        [HarmonyPatch(typeof(ResolutionConfigUtility), "UpdateDisplaySettings", new System.Type[] { typeof(Resolution) })]
        public static class UpdateDisplaySettings_Patch
        {
            public static void Postfix()
            {
                CursedUI.SetUpUIAppearance();
            }
        }
        #endregion
    }
    public static class ReceivedInfo
    {
        public static bool hasOpponent = false;
        public static bool opponentIsInBoss = false;
        public static int receivedScore = 0;
        public static int opponentHighscore = 0;
        public static int opponentHealth = 5;
    }
    public class CursedNetworking : MonoBehaviour
    {
        #region Public Variables
        public static bool isHost = false;
        public static bool playerDataChanged = true;
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
                playerDataChanged = true;
            }
            public string GetAsString(char divider)
            {
                string dataString = playerName + divider + inBoss + divider + highScore + divider + health;
                return dataString;
            }
        }
        public static PlayerPacket myPlayerPacket;
        #endregion

        public static void SetUpNetworking() //Called Once At Start (Includes Health Hard-Set)
        {
            System.Environment.SetEnvironmentVariable("SteamAppId", "3856460");
            System.Environment.SetEnvironmentVariable("SteamGameId", "3856460");

            MelonLogger.Msg("Steam Linked!");

            myPlayerPacket = new PlayerPacket("", 5);

            new CursedUI().SetUpUI();
        }
        public static void UpdateAndSendPlayerPacket()
        {
            if(CursedUI.lobbyID == CSteamID.Nil || !playerDataChanged) return;
            
            SteamMatchmaking.SetLobbyData(CursedUI.lobbyID, "PlayerPacket", myPlayerPacket.GetAsString(':'));
            MelonLogger.Msg("Updated Info To: " + myPlayerPacket.GetAsString(':'));
            playerDataChanged = false;
        }
        public static void ReceiveAndUpdateFoeInfo(LobbyDataUpdate_t callback)//HAS TESTING THING FOR SINGLE PLAYER!!!
        {
            if(callback.m_bSuccess == 0)
            {
                MelonLogger.Msg("Failed To Retrieve Data For Lobby: " + callback.m_ulSteamIDLobby);
                return;
            }

            string[] lobbyDataList = SteamMatchmaking.GetLobbyData((CSteamID)callback.m_ulSteamIDLobby, "PlayerPacket").Split(':');

            if(lobbyDataList.Count() == 4 && int.TryParse(lobbyDataList[2], out int highScoreInt) && int.TryParse(lobbyDataList[3], out int health))
            {
                if(lobbyDataList[0] != myPlayerPacket.playerName || true)//REMOVE "|| true" !!!
                {
                    ReceivedInfo.opponentIsInBoss = lobbyDataList[1] == "True";
                    ReceivedInfo.opponentHighscore = highScoreInt;
                    ReceivedInfo.opponentHealth = health;
                    MelonLogger.Msg("Received Info: " + string.Join(" | ", lobbyDataList));
                    if(!ReceivedInfo.hasOpponent)
                    {
                        ReceivedInfo.hasOpponent = true;
                        MelonLogger.Msg("You Now Have An Opponent!");
                    }
                }
                else
                {
                    MelonLogger.Msg("You Updated Info");
                }
            }
            else if(lobbyDataList.Count() == 4)
            {
                MelonLogger.Msg("Failed To Update Player Packet Info - Ints Didn't Parse");
            }

            if(ReceivedInfo.opponentIsInBoss)
            {
                MelonLogger.Msg("Opponent Is In Boss");
            }
        }
    }
    public class CursedUI
    {
        #region GameObjects
        public static GameObject canvasObj = new GameObject("Canvas", new System.Type[] { typeof(Canvas), typeof(RectTransform), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CursedUI), typeof(UnityEngine.UI.Image) });
        private static GameObject eventSystemObj = new GameObject("EventSystem", new System.Type[] { typeof(Transform), typeof(EventSystem), typeof(InputSystemUIInputModule), typeof(CursedUI) });
        private static GameObject lobbyMenuObj = new GameObject("Lobbies Menu", new System.Type[] { typeof(RectTransform), typeof(CursedUI) });
        private static GameObject scrollViewObj = new GameObject("Scorll View", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(CursedUI) });
        private static GameObject showLobbyButtonObj = new GameObject("Show Lobby Button", new System.Type[] {typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(TextMeshProUGUI), typeof(CursedUI) });
        private static GameObject hideLobbyButtonObj = new GameObject("Hide Lobby Button", new System.Type[] {typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(TextMeshProUGUI), typeof(CursedUI) });
        private static GameObject hostButtonObj = new GameObject("Host Button", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(TextMeshProUGUI), typeof(CursedUI) });
        private static GameObject lobbyIDObj = new GameObject("Lobby ID", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        private static GameObject lobbyIDBackgroundObj = new GameObject("Lobby ID Background", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(CursedUI) });
        private static GameObject lobbyButtonObj = new GameObject("Lobby Button", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(TextMeshProUGUI), typeof(CursedUI) });
        private static GameObject lobbyNameInputFieldObj = new GameObject("Lobby Name Input Field", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(TMP_InputField), typeof(CursedUI) });
        private static GameObject inputFieldTextObj = new GameObject("Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        private static GameObject inputFieldPlaceholderObj = new GameObject("Placeholder", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        private static GameObject joinLobbyButtonObj = new GameObject("Join Lobby Button", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(TextMeshProUGUI), typeof(CursedUI) });
        private static GameObject backButtonObj = new GameObject("Back Button", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(TextMeshProUGUI), typeof(CursedUI) });
        private static List<GameObject> lobbyObjects = new List<GameObject> { canvasObj, eventSystemObj, showLobbyButtonObj, hideLobbyButtonObj, hostButtonObj, lobbyIDObj, lobbyButtonObj, lobbyMenuObj, backButtonObj, lobbyNameInputFieldObj, joinLobbyButtonObj, lobbyIDBackgroundObj };
        public static GameObject waitingTextObj = new GameObject("Waiting Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        
        public static GameObject showLobbyButtonTextObj = new GameObject("Show Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public static GameObject hideLobbyButtonTextObj = new GameObject("Hide Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public static GameObject hostLobbyButtonTextObj = new GameObject("Host Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public static GameObject lobbyButtonTextObj = new GameObject("Lobby Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public static GameObject joinLobbyButtonTextObj = new GameObject("Join Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public static GameObject backLobbyButtonTextObj = new GameObject("Back Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        #endregion
        #region Steam Callbacks
        public static Callback<LobbyMatchList_t> m_lobbyMatchList;
        public static Callback<LobbyEnter_t> m_lobbyEnter;
        public static Callback<LobbyCreated_t> m_lobbyCreated;
        public static Callback<LobbyDataUpdate_t> m_updateData;
        #endregion
        public static List<CSteamID> listOfLobbies = new List<CSteamID>();
        public static CSteamID lobbyID;
        public static string lobbyName;
        private static void SetUIHeirarchy()
        {
            //Parenting
            inputFieldTextObj.transform.SetParent(lobbyNameInputFieldObj.transform);
            inputFieldPlaceholderObj.transform.SetParent(lobbyNameInputFieldObj.transform);
            joinLobbyButtonObj.transform.SetParent(lobbyNameInputFieldObj.transform);
            scrollViewObj.transform.SetParent(lobbyMenuObj.transform);
            lobbyNameInputFieldObj.transform.SetParent(lobbyMenuObj.transform);
            backButtonObj.transform.SetParent(lobbyMenuObj.transform);
            lobbyMenuObj.transform.SetParent(canvasObj.transform);
            hostButtonObj.transform.SetParent(canvasObj.transform);
            lobbyButtonObj.transform.SetParent(canvasObj.transform);
            showLobbyButtonObj.transform.SetParent(canvasObj.transform);
            hideLobbyButtonObj.transform.SetParent(canvasObj.transform);
            lobbyIDBackgroundObj.transform.SetParent(canvasObj.transform);
            lobbyIDObj.transform.SetParent(lobbyIDBackgroundObj.transform);
            waitingTextObj.transform.SetParent(canvasObj.transform);
                //Text Objects
                showLobbyButtonTextObj.transform.SetParent(showLobbyButtonObj.transform);
                hideLobbyButtonTextObj.transform.SetParent(hideLobbyButtonObj.transform);
                hostLobbyButtonTextObj.transform.SetParent(hostButtonObj.transform);
                lobbyButtonTextObj.transform.SetParent(lobbyButtonObj.transform);
                joinLobbyButtonTextObj.transform.SetParent(joinLobbyButtonObj.transform);
                backLobbyButtonTextObj.transform.SetParent(backButtonObj.transform);

            //Iteration Through All
            foreach(var thisObject in lobbyObjects)
            {
                //Persistence
                Object.DontDestroyOnLoad(thisObject);

                //hidden
                thisObject.SetActive(new List<GameObject>{ canvasObj, eventSystemObj, showLobbyButtonObj }.Contains(thisObject));
            }
            
            // Also mark input field text components for persistence
            Object.DontDestroyOnLoad(inputFieldTextObj);
            Object.DontDestroyOnLoad(inputFieldPlaceholderObj);

            //Canvas Stuff
            UnityEngine.UI.Image canvasImage = canvasObj.GetComponent<UnityEngine.UI.Image>();
            if(canvasImage != null)
            {
                canvasImage.color = new UnityEngine.Color(0, 0, 0, 0);
                canvasImage.raycastTarget = true;
            }
            canvasObj.GetComponent<Canvas>().sortingLayerID = SortingLayer.layers.Count() - 1;
            canvasObj.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            try
            {
                canvasObj.GetComponent<CanvasScaler>().referenceResolution = new Vector2(Screen.width * 2, Screen.height * 2);
                canvasObj.GetComponent<CanvasScaler>().matchWidthOrHeight = 0.5f;
            }
            catch(System.Exception e)
            {
                MelonLogger.Msg(e);
            }
            canvasObj.GetComponent<Canvas>().sortingOrder = 999;
            canvasObj.GetComponent<Image>().enabled = false;
        }
        public static void SetUpUIAppearance()
        {
            SetUIHeirarchy();

            canvasObj.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObj.GetComponent<CanvasScaler>().referenceResolution = new Vector2(Screen.width, Screen.height);
            
            #region Text Stuff
            RectTransform lobbyIDRect = lobbyIDObj.GetComponent<RectTransform>();
            if(lobbyIDRect != null)
            {
                lobbyIDRect.localPosition = new Vector3(0, 0, 0);
                lobbyIDRect.sizeDelta = new Vector2(800, 150);
            }
                TextMeshProUGUI lobbyIDText = lobbyIDObj.GetComponent<TextMeshProUGUI>();
                if(lobbyIDText != null)
                {
                    lobbyIDText.color = new Color32(255, 255, 255, 255); 
                    lobbyIDText.fontSize = 100;
                    lobbyIDText.alignment = TextAlignmentOptions.Center;
                }
            RectTransform lobbyIDBackgroundRect = lobbyIDBackgroundObj.GetComponent<RectTransform>();
            if(lobbyIDBackgroundRect != null)
            {
                lobbyIDBackgroundRect.localPosition = new Vector3(0, Screen.height / 4, 0);
                lobbyIDBackgroundRect.sizeDelta = new Vector2(800, 150);
            }
                UnityEngine.UI.Image lobbyIDBackgroundImg = lobbyIDBackgroundObj.GetComponent<UnityEngine.UI.Image>();
                if(lobbyIDBackgroundImg != null)
                {
                    lobbyIDBackgroundImg.color = new UnityEngine.Color(0.1f, 0.1f, 0.1f, 1);
                }
            RectTransform waitingTextRect = waitingTextObj.GetComponent<RectTransform>();
            if(waitingTextRect != null)
            {
                waitingTextRect.localPosition = new Vector3(0, 0);
                waitingTextRect.sizeDelta = new Vector2(500, 100);
            }
                TextMeshProUGUI waitingText = waitingTextObj.GetComponent<TextMeshProUGUI>();
                if(waitingText != null)
                {
                    waitingText.text = "Waiting For Opponent...";
                    waitingText.autoSizeTextContainer = true;
                    waitingText.alignment = TextAlignmentOptions.Center;
                }
            waitingTextObj.SetActive(false);

            TextMeshProUGUI showLobbyButtonText = showLobbyButtonTextObj.GetComponent<TextMeshProUGUI>();
            if(showLobbyButtonText != null)
            {
                showLobbyButtonText.text = "Open";
                showLobbyButtonText.fontSize = 50;
                showLobbyButtonText.alignment = TextAlignmentOptions.Center;
            }
            TextMeshProUGUI hideLobbyButtonText = hideLobbyButtonTextObj.GetComponent<TextMeshProUGUI>();
            if(hideLobbyButtonText != null)
            {
                hideLobbyButtonText.text = "Close";
                hideLobbyButtonText.fontSize = 50;
                hideLobbyButtonText.alignment = TextAlignmentOptions.Center;
            }
            TextMeshProUGUI hostLobbyButtonText = hostLobbyButtonTextObj.GetComponent<TextMeshProUGUI>();
            if(hostLobbyButtonText != null)
            {
                hostLobbyButtonText.text = "Host Lobby";
                hostLobbyButtonText.fontSize = 100;
                hostLobbyButtonText.alignment = TextAlignmentOptions.Center;
            }
            TextMeshProUGUI lobbyButtonText = lobbyButtonTextObj.GetComponent<TextMeshProUGUI>();
            if(lobbyButtonText != null)
            {
                lobbyButtonText.text = "Find Lobby";
                lobbyButtonText.fontSize = 100;
                lobbyButtonText.alignment = TextAlignmentOptions.Center;
            }
            TextMeshProUGUI joinLobbyButtonText = joinLobbyButtonTextObj.GetComponent<TextMeshProUGUI>();
            if(joinLobbyButtonText != null)
            {
                joinLobbyButtonText.text = "Join";
                joinLobbyButtonText.fontSize = 75;
                joinLobbyButtonText.alignment = TextAlignmentOptions.Center;
            }
            TextMeshProUGUI backLobbyButtonText = backLobbyButtonTextObj.GetComponent<TextMeshProUGUI>();
            if(backLobbyButtonText != null)
            {
                backLobbyButtonText.text = "Leave";
                backLobbyButtonText.fontSize = 50;
                backLobbyButtonText.alignment = TextAlignmentOptions.Center;
            }
            #endregion
            #region Buttons Appearance
            RectTransform showLobbyButtonRect = showLobbyButtonObj.GetComponent<RectTransform>();
            if(showLobbyButtonRect != null)
            {
                showLobbyButtonRect.localPosition = new Vector3(-1 * Screen.width * 3 / 8, Screen.height * 3 / 8, 0);
                showLobbyButtonRect.sizeDelta = new Vector2(200, 100);
            }
                UnityEngine.UI.Image showLobbyButtonImg = showLobbyButtonObj.GetComponent<Image>();
                if(showLobbyButtonImg != null)
                {
                    showLobbyButtonImg.color = new UnityEngine.Color(0.25f, 0.25f, 0.25f, 0.95f);
                }
            RectTransform hideLobbyButtonRect = hideLobbyButtonObj.GetComponent<RectTransform>();
            if(hideLobbyButtonRect != null)
            {
                hideLobbyButtonRect.localPosition = new Vector3(-1 * Screen.width * 3 / 8, Screen.height * 3 / 8, 0);
                hideLobbyButtonRect.sizeDelta = new Vector2(200, 100);
            }
                UnityEngine.UI.Image hideLobbyButtonImg = hideLobbyButtonObj.GetComponent<Image>();
                if(hideLobbyButtonImg != null)
                {
                    hideLobbyButtonImg.color = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);
                }
            RectTransform hostButtonRect = hostButtonObj.GetComponent<RectTransform>();
            if(hostButtonRect != null)
            {
                hostButtonRect.localPosition = new Vector3(0, Screen.height / 10, 0);
                hostButtonRect.sizeDelta = new Vector2(600, 150);
            }
                UnityEngine.UI.Image hostButtonImg = hostButtonObj.GetComponent<Image>();
                if(hostButtonImg != null)
                {
                    hostButtonImg.color = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);
                }
                RectTransform hostTextRect = hostLobbyButtonTextObj.GetComponent<RectTransform>();
                if(hostTextRect != null)
                {
                    hostTextRect.sizeDelta = new Vector2(600, 150);
                }
            RectTransform lobbyButtonRect = lobbyButtonObj.GetComponent<RectTransform>();
            if(lobbyButtonRect != null)
            {
                lobbyButtonRect.localPosition = new Vector3(0, -1 * Screen.height / 10, 0);
                lobbyButtonRect.sizeDelta = new Vector2(600, 150);
            }
                UnityEngine.UI.Image lobbyButtonImg = lobbyButtonObj.GetComponent<Image>();
                if(lobbyButtonImg != null)
                {
                    lobbyButtonImg.color = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);
                }
                RectTransform lobbyTextRect = lobbyButtonTextObj.GetComponent<RectTransform>();
                if(lobbyTextRect != null)
                {
                    lobbyTextRect.sizeDelta = new Vector2(600, 150);
                }
            RectTransform backButtonRect = backButtonObj.GetComponent<RectTransform>();
            if(backButtonRect != null)
            {
                backButtonRect.localPosition = new Vector3(-1 * Screen.width * 3 / 8, -1 * Screen.height * 3 / 8, 0);
                backButtonRect.sizeDelta = new Vector2(150, 70);
            }
                UnityEngine.UI.Image backButtonImg = backButtonObj.GetComponent<Image>();
                if(backButtonImg != null)
                {
                    backButtonImg.color = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);
                }
            RectTransform inputFieldRect = lobbyNameInputFieldObj.GetComponent<RectTransform>();
            if(inputFieldRect != null)
            {
                inputFieldRect.localPosition = new Vector3(0, 200, 0);
                inputFieldRect.sizeDelta = new Vector2(800, 150);
            }
            RectTransform joinButtonRect = joinLobbyButtonObj.GetComponent<RectTransform>();
            if(joinButtonRect != null)
            {
                joinButtonRect.localPosition = new Vector3(0, -150, 0);
                joinButtonRect.sizeDelta = new Vector2(400, 100);
            }
                UnityEngine.UI.Image joinButtonImage = joinLobbyButtonObj.GetComponent<UnityEngine.UI.Image>();
                if(joinButtonImage != null)
                {
                    joinButtonImage.color = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);
                }
                TextMeshProUGUI joinButtonText = joinLobbyButtonObj.GetComponent<TextMeshProUGUI>();
                if(joinButtonText != null)
                {
                    joinButtonText.text = "Join Lobby";
                    joinButtonText.color = new UnityEngine.Color(1, 1, 1, 1);
                    joinButtonText.alignment = TextAlignmentOptions.Center;
                }
            #endregion
            #region Other Appearance Stuff
            RectTransform scrollViewRect = scrollViewObj.GetComponent<RectTransform>();
            if(scrollViewRect != null)
            {
                scrollViewRect.localPosition = new Vector3(0, 0, 0);
                scrollViewRect.sizeDelta = new Vector2(Screen.width, Screen.height);
            }
                UnityEngine.UI.Image scrollViewImg = scrollViewObj.GetComponent<UnityEngine.UI.Image>();
                if (scrollViewImg != null)
                {
                    scrollViewImg.color = new UnityEngine.Color(0.25f, 0.25f, 0.25f, 0.95f);
                }
            #endregion
            #region Input Field
            UnityEngine.UI.Image inputFieldImage = lobbyNameInputFieldObj.GetComponent<UnityEngine.UI.Image>();
            if(inputFieldImage != null)
            {
                inputFieldImage.color = new UnityEngine.Color(0.15f, 0.15f, 0.15f, 0.9f);
            }
            TMP_InputField inputField = lobbyNameInputFieldObj.GetComponent<TMP_InputField>();
            if(inputField != null)
            {
                // Setup text component
                inputFieldTextObj.transform.localScale = Vector3.one;
                RectTransform textRect = inputFieldTextObj.GetComponent<RectTransform>();
                if(textRect != null)
                {
                    inputFieldRect.localPosition = new Vector3(0, 2 * Screen.height / 10, 0);
                    inputFieldRect.sizeDelta = new Vector2(800, 150);
                }
                TextMeshProUGUI textComponent = inputFieldTextObj.GetComponent<TextMeshProUGUI>();
                if(textComponent != null)
                {
                    textComponent.color = new UnityEngine.Color(1, 1, 1, 1);
                    textComponent.alignment = TextAlignmentOptions.Center;
                    textComponent.fontSize = 100;
                }
                
                // Setup placeholder component
                inputFieldPlaceholderObj.transform.localScale = Vector3.one;
                RectTransform placeholderRect = inputFieldPlaceholderObj.GetComponent<RectTransform>();
                if(placeholderRect != null)
                {
                    inputFieldRect.localPosition = new Vector3(0, 1 * Screen.height / 10, 0);
                    inputFieldRect.sizeDelta = new Vector2(800, 150);
                    placeholderRect.sizeDelta = inputFieldRect.sizeDelta;
                }
                TextMeshProUGUI placeholder = inputFieldPlaceholderObj.GetComponent<TextMeshProUGUI>();
                if(placeholder != null)
                {
                    placeholder.text = "Enter Lobby ID";
                    placeholder.color = new UnityEngine.Color(0.7f, 0.7f, 0.7f, 0.5f);
                    placeholder.alignment = TextAlignmentOptions.Center;
                    placeholder.fontSize = 100;
                }
                
                // Setup input field properties
                inputField.textComponent = textComponent;
                inputField.placeholder = placeholder;
                inputField.caretColor = new UnityEngine.Color(1, 1, 1, 1);
                inputField.caretWidth = 1;
                inputField.selectionColor = new UnityEngine.Color(0.65f, 0.8f, 1, 0.75f);
            }
            #endregion
        }
        public void SetUpUI() //Called Once On Game Start
        {
            SetUpUIAppearance();
            
            #region Buttons Callbacks
            Button lobbyButton = lobbyButtonObj.GetComponent<Button>();
            if(lobbyButton != null)
            {
                lobbyButton.onClick.AddListener(GetLobbiesList);
            }
            Button showLobbyButton = showLobbyButtonObj.GetComponent<Button>();
            if(showLobbyButton != null)
            {
                showLobbyButton.onClick.AddListener(OpenLobbyStuff);
            }
            Button hideLobbyButton = hideLobbyButtonObj.GetComponent<Button>();
            if(hideLobbyButton != null)
            {
                hideLobbyButton.onClick.AddListener(CloseLobbyStuff);
            }
            Button hostButton = hostButtonObj.GetComponent<Button>();
            if(hostButton != null)
            {
                hostButton.onClick.AddListener(HostLobby);
            }
            Button backButton = backButtonObj.GetComponent<Button>();
            if(backButton != null)
            {
                backButton.onClick.AddListener(BackButtonPressed);
            }
            Button joinLobbyButton = joinLobbyButtonObj.GetComponent<Button>();
            if(joinLobbyButton != null)
            {
                joinLobbyButton.onClick.AddListener(TryJoinLobby);
            }
            #endregion
            
            #region Steam Callbacks
            m_lobbyMatchList = Callback<LobbyMatchList_t>.Create(OnLobbyMatchList);
            m_lobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            m_updateData = Callback<LobbyDataUpdate_t>.Create(CursedNetworking.ReceiveAndUpdateFoeInfo);
            m_lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            #endregion
        }

        #region Button Callbacks
        public static void GetLobbiesList()
        {
            foreach(var thisObject in lobbyObjects)
            {
                thisObject.SetActive(!new List<GameObject>{ hostButtonObj, lobbyButtonObj, showLobbyButtonObj, lobbyIDBackgroundObj }.Contains(thisObject));
            }
        }
        public static void OpenLobbyStuff()
        {
            if(lobbyID != CSteamID.Nil)
            {
                foreach(var thisObject in lobbyObjects)
                {
                    thisObject.SetActive(!new List<GameObject>{ showLobbyButtonObj, lobbyNameInputFieldObj, hostButtonObj, lobbyButtonObj }.Contains(thisObject));
                    canvasObj.GetComponent<UnityEngine.UI.Image>().enabled = true;
                }
                return;
            }

            lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "Waiting...";
            foreach(var thisObject in lobbyObjects)
            {
                thisObject.SetActive(!new List<GameObject>{ showLobbyButtonObj, backButtonObj, lobbyNameInputFieldObj, lobbyIDBackgroundObj }.Contains(thisObject));
                canvasObj.GetComponent<UnityEngine.UI.Image>().enabled = true;
            }
        }
        public static void CloseLobbyStuff()
        {
            foreach(var thisObject in lobbyObjects)
            {
                thisObject.SetActive(new List<GameObject>{ canvasObj, eventSystemObj, showLobbyButtonObj }.Contains(thisObject));
                canvasObj.GetComponent<UnityEngine.UI.Image>().enabled = false;
            }
        }
        public static void BackButtonPressed()
        {
            ReceivedInfo.hasOpponent = false;
            ReceivedInfo.opponentHealth = 5;
            ReceivedInfo.opponentHighscore = 0;
            ReceivedInfo.opponentIsInBoss = false;
            foreach(var thisObject in lobbyObjects)
            {
                thisObject.SetActive(!new List<GameObject> { backButtonObj, showLobbyButtonObj, lobbyNameInputFieldObj, lobbyIDBackgroundObj }.Contains(thisObject));
            }
            lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "Waiting...";
            if(lobbyName != "")
            {
                SteamMatchmaking.LeaveLobby(lobbyID);
                lobbyID = CSteamID.Nil;
                lobbyName = "";
            }
            CursedNetworking.isHost = false;
        }
        public static void TryJoinLobby()
        {
            foreach (var thisObject in lobbyObjects)
            {
                thisObject.SetActive(!new List<GameObject> { hostButtonObj, lobbyButtonObj, showLobbyButtonObj, lobbyNameInputFieldObj }.Contains(thisObject));
            }
            try
            {
                string inputLobbyCode = lobbyNameInputFieldObj.GetComponent<TMP_InputField>().text.Trim();

                if(inputLobbyCode == "")
                {
                    MelonLogger.Msg("Getting Random Lobby");
                }
                
                lobbyName = "Random";
                SteamMatchmaking.AddRequestLobbyListResultCountFilter(100);
                SteamAPICall_t lobbyRequest = SteamMatchmaking.RequestLobbyList();
            }
            catch(System.Exception e)
            {
                MelonLogger.Msg(e);
            }
        }
        public static void HostLobby()
        {
            foreach (var thisObject in lobbyObjects)
            {
                thisObject.SetActive(!new List<GameObject> { hostButtonObj, lobbyButtonObj, showLobbyButtonObj, lobbyNameInputFieldObj }.Contains(thisObject));
            }
            CursedNetworking.isHost = true;
            CreateLobby();
        }
        private static void CreateLobby()
        {
            
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, 100);

            if(CursedNetworking.myPlayerPacket.playerName == "" || CursedNetworking.myPlayerPacket.playerName == null)
            {
                CursedNetworking.myPlayerPacket.playerName = "Player 1";
            }
            CursedNetworking.isHost = true;
        }
        #endregion

        #region Steamworks Callbacks
        void OnLobbyMatchList(LobbyMatchList_t callback)
        {
            listOfLobbies.Clear();

            for (int i = 0; i < callback.m_nLobbiesMatching; i++)
            {
                CSteamID tempLobbyID = SteamMatchmaking.GetLobbyByIndex(i);
                listOfLobbies.Add(tempLobbyID);
            }
            string inputLobbyCode = lobbyNameInputFieldObj.GetComponent<TMP_InputField>().text.Trim();
            foreach(var lobby in listOfLobbies)
            {
                ulong thisLobbyID = lobby.m_SteamID;
                if((thisLobbyID % 10000).ToString("D4") == inputLobbyCode)
                {
                    MelonLogger.Msg("Found Chosen Lobby! Joining...");
                    SteamMatchmaking.JoinLobby(lobby);
                    return;
                }
                else if(inputLobbyCode == "" && SteamMatchmaking.GetNumLobbyMembers(lobby) == 1)
                {
                    MelonLogger.Msg("Joining Random Lobby");
                    SteamMatchmaking.JoinLobby(lobby);
                    return;
                }
            }
            MelonLogger.Msg("Failed To Get A Lobby To Join");
            if(inputLobbyCode == "")
                lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "No Lobby Found";
            else
                lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "Lobby Not Found";
        }
        void OnLobbyEnter(LobbyEnter_t callback)
        {
            if(CursedNetworking.isHost) return;
            MelonLogger.Msg("Joined Lobby");

            if(CursedNetworking.myPlayerPacket.playerName == "" || CursedNetworking.myPlayerPacket.playerName == null)
            {
                CursedNetworking.myPlayerPacket.playerName = "Player 2";
                MelonLogger.Msg("You Are Player 2");
            }

            if(callback.m_EChatRoomEnterResponse != 1)
            {
                MelonLogger.Msg("Failed To Enter Lobby: " + (uint)callback.m_EChatRoomEnterResponse);
                return;
            }
            lobbyID = (CSteamID)callback.m_ulSteamIDLobby;
            lobbyName = ((ulong)lobbyID % 10000).ToString("D4");
            lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "Lobby ID: " + lobbyName;
        }
        void OnLobbyCreated(LobbyCreated_t callback)
        {
            if(callback.m_eResult != EResult.k_EResultOK)
            {
                MelonLogger.Msg("Error: Lobby Creation Failed");
                return;
            }

            lobbyID = (CSteamID)callback.m_ulSteamIDLobby;
            lobbyName = ((ulong)lobbyID % 10000).ToString("D4");
            lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "Lobby ID: " + lobbyName;
            
            MelonLogger.Msg("Lobby Created: " + lobbyID);
        }
        public static void DisableCallbacks()
        {
            m_lobbyEnter.Dispose();
            m_lobbyMatchList.Dispose();
            m_updateData.Dispose();
            m_lobbyCreated.Dispose();
        }
        #endregion
    
        #region In-Game UI Stuff
        private static GameObject myHeartsObj = new GameObject("Hearts", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(Image) });
        private static GameObject foeHeartsObj = new GameObject("Hearts", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(Image) });
        private static List<GameObject> UIObjects = new List<GameObject> { myHeartsObj, foeHeartsObj };

        public static void ToggleOverlay(bool turnOn)
        {
            foreach(GameObject thisObject in UIObjects)
            {

            }
        }
        #endregion
    }
}