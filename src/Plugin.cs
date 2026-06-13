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
        [HarmonyPatch(typeof(EncounterController), "SubmitWord", new System.Type[] {typeof(List<TileSelection>), typeof(List<string>)})]
        public static class SubmitWord_Patch
        {
            private static async Task AsyncronousWaiting(EncounterController encounterController, List<TileSelection> tiles, List<string> words)
            {
                await Task.Delay(100);
                encounterController.SubmitWord(tiles, words);
                CursedUI.waitingTextObj.SetActive(true);
                MelonLogger.Msg("Waiting For Opponent...");
            }
            public static bool Prefix(ref int ____remainingGrids, ref EncounterController __instance, ref List<TileSelection> tiles, ref List<string> words)
            {
                if(____remainingGrids <= 0 && ReceivedInfo.hasOpponent)
                {
                    if(CursedNetworking.myPlayerPacket.inBoss) MelonLogger.Msg("Finished Battle!");
                    CursedNetworking.myPlayerPacket.UpdatePacket(false, CursedNetworking.myPlayerPacket.highScore, CursedNetworking.myPlayerPacket.health);
                    //Freezes Game Until Opponent Gets There
                    if(ReceivedInfo.opponentHighscore == 0 || ReceivedInfo.opponentIsInBoss) //haven't gotten to or are in boss (in boss has high score, out of it doesn't)
                    {
                        AsyncronousWaiting(__instance, tiles, words);
                        return false;
                    }
                    CursedUI.waitingTextObj.SetActive(false);
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
                    if(mostRecentScorePacket.Score > CursedNetworking.myPlayerPacket.highScore) CursedNetworking.myPlayerPacket.UpdatePacket(CursedNetworking.myPlayerPacket.inBoss, (int)mostRecentScorePacket.Score, CursedNetworking.myPlayerPacket.health);
                }
                else if(CursedNetworking.myPlayerPacket.highScore > 0)
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
                MelonLogger.Msg("Most Recent Score: " + __result);
                mostRecentScorePacket = __result;
            }
        }
        //Get Remaining Target (SubmitWord_Ptch moved into boss stuff)
        #endregion
        public override void OnUpdate()
        {
            base.OnUpdate();
            if(SteamAPI.Init()) SteamAPI.RunCallbacks();
            if(CursedUI.lobbyID != CSteamID.Nil) CursedNetworking.UpdateAndSendPlayerPacket();
            ReceivedInfo.ReceivedInfoUpdate();
            if(SteamMatchmaking.GetNumLobbyMembers(CursedUI.lobbyID) > 1 && !ReceivedInfo.hasOpponent)
            {
                ReceivedInfo.hasOpponent = true;
                MelonLogger.Msg("2 People In Lobby!");
            }
        }
    }
    public static class ReceivedInfo
    {
        public static bool hasOpponent = true;
        public static bool opponentIsInBoss = true;
        public static int receivedScore = 0;
        public static int opponentHighscore = 0;
        public static int opponentHealth = 5;
        public static bool somethingUpdated = true, somethingShouldUpdate = true;
        public static void ReceivedInfoUpdate()
        {
            if(somethingShouldUpdate) Unupdate();
            if(somethingUpdated) somethingShouldUpdate = true;
        }
        private static void Unupdate()
        {
            somethingUpdated = false;
            somethingShouldUpdate = false;
        }
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
            
            SteamMatchmaking.SetLobbyMemberData(CursedUI.lobbyID, "PlayerPacket", myPlayerPacket.GetAsString(':'));
            MelonLogger.Msg("Updated Info To: " + myPlayerPacket.GetAsString(':'));
            playerDataChanged = false;
        }
        public static void ReceiveAndUpdateFoeInfo(LobbyDataUpdate_t callback)
        {
            if(callback.m_bSuccess == 0)
            {
                MelonLogger.Msg("Failed To Retrieve Data For Lobby: " + callback.m_ulSteamIDLobby);
            }

            string lobbyData = SteamMatchmaking.GetLobbyData((CSteamID)callback.m_ulSteamIDLobby, "PlayerPacket");
            string[] lobbyDataList = lobbyData.Split(':');
            MelonLogger.Msg("Received Info: " + string.Join("\n", lobbyDataList));

            if(lobbyData.Count() == 4 && int.TryParse(lobbyDataList[2], out int highScoreInt) && int.TryParse(lobbyDataList[3], out int health))
            {
                if(lobbyDataList[0] != myPlayerPacket.playerName)
                {
                    ReceivedInfo.hasOpponent = true;
                    ReceivedInfo.opponentIsInBoss = lobbyDataList[1] == "true";
                    ReceivedInfo.opponentHighscore = highScoreInt;
                    ReceivedInfo.opponentHealth = health;
                    MelonLogger.Msg(string.Join("\n", lobbyDataList));
                }
                else
                {
                    MelonLogger.Msg("You Updated Info");
                }
            }
            else if(lobbyData.Count() == 4)
            {
                MelonLogger.Msg("Failed To Update Player Packet Info - Ints Didn't Parse");
            }

            if(ReceivedInfo.opponentIsInBoss)
            {
                MelonLogger.Msg("Opponent Is In Boss");
            }
        }
    }
    public class CursedUI //CANVAS NEEDS FIXING ON LINE 360 FOR IT TO SHOW UP!!!
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
        private static GameObject backButtonObj = new GameObject("Lobby Button", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(TextMeshProUGUI), typeof(CursedUI) });
        private static List<GameObject> lobbyObjects = new List<GameObject> { canvasObj, eventSystemObj, showLobbyButtonObj, hideLobbyButtonObj, hostButtonObj, lobbyIDObj, lobbyButtonObj, lobbyMenuObj, backButtonObj, lobbyNameInputFieldObj, joinLobbyButtonObj, lobbyIDBackgroundObj };
        public static GameObject waitingTextObj = new GameObject("Waiting Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
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
            canvasObj.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            try
            {
                canvasObj.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
                canvasObj.GetComponent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
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

            #region Text Stuff
            RectTransform lobbyIDRect = lobbyIDObj.GetComponent<RectTransform>();
            if(lobbyIDRect != null)
            {
                lobbyIDRect.position = new Vector3(0, 0, 0);
                lobbyIDRect.sizeDelta = new Vector2(100, 1);
            }
                TextMeshProUGUI lobbyIDText = lobbyIDObj.GetComponent<TextMeshProUGUI>();
                if(lobbyIDText != null)
                {
                    lobbyIDText.color = new Color32(255, 255, 255, 255); 
                    lobbyIDText.fontSize = 1;
                    lobbyIDText.alignment = TextAlignmentOptions.Center;
                }
            RectTransform lobbyIDBackgroundRect = lobbyIDBackgroundObj.GetComponent<RectTransform>();
            if(lobbyIDBackgroundRect != null)
            {
                lobbyIDBackgroundRect.position = new Vector3(0, 5, 0);
                lobbyIDBackgroundRect.sizeDelta = new Vector2(12, 2);
            }
                UnityEngine.UI.Image lobbyIDBackgroundImg = lobbyIDBackgroundObj.GetComponent<UnityEngine.UI.Image>();
                if(lobbyIDBackgroundImg != null)
                {
                    lobbyIDBackgroundImg.color = new UnityEngine.Color(0.1f, 0.1f, 0.1f, 1);
                }
            RectTransform waitingTextRect = waitingTextObj.GetComponent<RectTransform>();
            if(waitingTextRect != null)
            {
                waitingTextRect.position = new Vector3(0, 0);
                waitingTextRect.sizeDelta = new Vector2(100, 1);
            }
                TextMeshProUGUI waitingText = waitingTextObj.GetComponent<TextMeshProUGUI>();
                if(waitingText != null)
                {
                    waitingText.text = "Waiting For Opponent...";
                    waitingText.autoSizeTextContainer = false;
                    waitingText.fontSize = 1;
                    waitingText.alignment = TextAlignmentOptions.Center;
                }
            waitingTextObj.SetActive(false);
            #endregion
            #region Buttons Appearance
            RectTransform showLobbyButtonRect = showLobbyButtonObj.GetComponent<RectTransform>();
            if(showLobbyButtonRect != null)
            {
                showLobbyButtonRect.position = new Vector3(-8f, 5.75f,0);
                showLobbyButtonRect.sizeDelta = new Vector2(1.5f, 0.9f);
            }
            RectTransform hideLobbyButtonRect = hideLobbyButtonObj.GetComponent<RectTransform>();
            if(hideLobbyButtonRect != null)
            {
                hideLobbyButtonRect.position = new Vector3(-8f, 5.75f,0);
                hideLobbyButtonRect.sizeDelta = new Vector2(1.5f, 0.9f);
            }
            RectTransform hostButtonRect = hostButtonObj.GetComponent<RectTransform>();
            if(hostButtonRect != null)
            {
                hostButtonRect.position = new Vector3(0, 2, 0);
                hostButtonRect.sizeDelta = new Vector2(5, 1.5f);
            }
            RectTransform lobbyButtonRect = lobbyButtonObj.GetComponent<RectTransform>();
            if(lobbyButtonRect != null)
            {
                lobbyButtonRect.position = new Vector3(0, 0, 0);
                lobbyButtonRect.sizeDelta = new Vector2(5, 1.5f);
            }
            RectTransform backButtonRect = backButtonObj.GetComponent<RectTransform>();
            if(backButtonRect != null)
            {
                backButtonRect.position = new Vector3(-6, -3, 0);
                backButtonRect.sizeDelta = new Vector2(1.5f, 0.9f);
            }
            RectTransform inputFieldRect = lobbyNameInputFieldObj.GetComponent<RectTransform>();
            if(inputFieldRect != null)
            {
                inputFieldRect.position = new Vector3(0, 3, 0);
                inputFieldRect.sizeDelta = new Vector2(10, 1.5f);
            }
            RectTransform joinButtonRect = joinLobbyButtonObj.GetComponent<RectTransform>();
            if(joinButtonRect != null)
            {
                joinButtonRect.position = new Vector3(0, 1, 0);
                joinButtonRect.sizeDelta = new Vector2(5, 1);
            }
            #endregion
            #region Other Appearance Stuff
            RectTransform scrollViewRect = scrollViewObj.GetComponent<RectTransform>();
            if(scrollViewRect != null)
            {
                scrollViewRect.position = new Vector3(0, 1, 0);
                scrollViewRect.sizeDelta = new Vector2(20, 15);
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
                inputFieldRect.position = new Vector3(0, 3, 0);
                inputFieldRect.sizeDelta = new Vector2(10, 1.5f);
                }
                TextMeshProUGUI textComponent = inputFieldTextObj.GetComponent<TextMeshProUGUI>();
                if(textComponent != null)
                {
                    textComponent.color = new UnityEngine.Color(1, 1, 1, 1);
                    textComponent.alignment = TextAlignmentOptions.Center;
                    textComponent.fontSize = 1;
                }
                
                // Setup placeholder component
                inputFieldPlaceholderObj.transform.localScale = Vector3.one;
                RectTransform placeholderRect = inputFieldPlaceholderObj.GetComponent<RectTransform>();
                if(placeholderRect != null)
                {
                inputFieldRect.position = new Vector3(0, 3, 0);
                inputFieldRect.sizeDelta = new Vector2(10, 1.5f);
                }
                TextMeshProUGUI placeholder = inputFieldPlaceholderObj.GetComponent<TextMeshProUGUI>();
                if(placeholder != null)
                {
                    placeholder.text = "Enter Lobby ID";
                    placeholder.color = new UnityEngine.Color(0.7f, 0.7f, 0.7f, 0.5f);
                    placeholder.alignment = TextAlignmentOptions.Center;
                    placeholder.fontSize = 1;
                }
                
                // Setup input field properties
                inputField.textComponent = textComponent;
                inputField.placeholder = placeholder;
                inputField.caretColor = new UnityEngine.Color(1, 1, 1, 1);
                inputField.caretWidth = 1;
                inputField.selectionColor = new UnityEngine.Color(0.65f, 0.8f, 1, 0.75f);
            }
            
            // Join Button Styling
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

            foreach(var lobby in listOfLobbies)
            {
                MelonLogger.Msg("Joining Lobby");
                SteamMatchmaking.JoinLobby(lobby);
                return;
            }
            MelonLogger.Msg("Failed To Get A Lobby To Join");
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

            SteamMatchmaking.SetLobbyData(lobbyID, "LobbyName", lobbyName);
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
    }
}