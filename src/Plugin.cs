using MelonLoader;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Steamworks;
using System.Linq;
using UnityEngine.InputSystem.UI;
using System.Security.Policy;

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

        public override void OnUpdate()
        {
            base.OnUpdate();
            if(SteamAPI.Init()) SteamAPI.RunCallbacks();
        }
    }
    public static class ReceivedInfo
    {
        public static bool hasOpponent = false;
        public static bool opponentIsInBoss = false;
        public static int receivedScore = 0;
        public static int opponentHighscore = 1;
        public static int opponentHealth = 5;
    }
    public class CursedNetworking : MonoBehaviour
    {
        #region Public Variables
        public static bool isHost = false;
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
        public static PlayerPacket myPlayerPacket;
        #endregion

        public static void SetUpNetworking() //Simulates OnEnable
        {
            System.Environment.SetEnvironmentVariable("SteamAppId", "3856460");
            System.Environment.SetEnvironmentVariable("SteamGameId", "3856460");

            MelonLogger.Msg("Steam Linked!");

            new CursedUI().SetUpUI();
        }
    }
    public class CursedUI
    {
        #region GameObjects
        private static GameObject canvasObj = new GameObject("Canvas", new System.Type[] { typeof(Canvas), typeof(RectTransform), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CursedUI), typeof(UnityEngine.UI.Image) });
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
        #endregion
        #region Steam Callbacks
        public static CallResult<LobbyMatchList_t> m_lobbyMatchList;
        public static CallResult<LobbyEnter_t> m_lobbyEnter;
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
            canvasObj.GetComponent<Canvas>().sortingLayerID = SortingLayer.layers.Count();
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
        public void SetUpUI()
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
            m_lobbyMatchList = CallResult<LobbyMatchList_t>.Create(OnLobbyMatchList);
            m_lobbyEnter = CallResult<LobbyEnter_t>.Create(OnLobbyEnter);
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
            foreach(var thisObject in lobbyObjects)
            {
                thisObject.SetActive(!new List<GameObject> { backButtonObj, showLobbyButtonObj, lobbyNameInputFieldObj, lobbyIDBackgroundObj }.Contains(thisObject));
            }
            lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "Waiting...";
            if(lobbyName != "")
            {
                SteamMatchmaking.LeaveLobby(lobbyID);
                lobbyName = "";
            }
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
                m_lobbyMatchList.Set(lobbyRequest);
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
            CreateLobby();
        }
        private static void CreateLobby()
        {
            
            try
            {
                SteamAPICall_t newLobby = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, 100);
                lobbyID = (CSteamID)newLobby.m_SteamAPICall;

                //make lobby name
                string alphaneumerics = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                lobbyName = "";
                for(int i = 0; i < 6; i++)
                {
                    lobbyName += alphaneumerics[Random.Range(0, alphaneumerics.Length)];
                }

                lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "Lobby Name: N/A";
                SteamMatchmaking.SetLobbyData(lobbyID, "LobbyName", lobbyName);
                MelonLogger.Msg("Lobby Code: Not Working");
            }
            catch(System.Exception e)
            {
                MelonLogger.Msg(e);
            }

            SteamAPICall_t lobbyRequest = SteamMatchmaking.RequestLobbyList();
            m_lobbyMatchList.Set(lobbyRequest);

            CursedNetworking.isHost = true;
            if(CursedNetworking.myPlayerPacket.playerName == "")
            {
                CursedNetworking.myPlayerPacket.playerName = "Player 1";
            }
        }
        void OnLobbyMatchList(LobbyMatchList_t pCallback, bool bIOFailure)
        {
            MelonLogger.Msg("Getting A List Of Lobbies");

            if(bIOFailure)
            {
                MelonLogger.Msg("Failed To Reach Steam Matchmaking");
                return;
            }

            listOfLobbies.Clear();

            for (int i = 0; i < pCallback.m_nLobbiesMatching; i++)
            {
                CSteamID tempLobbyID = SteamMatchmaking.GetLobbyByIndex(i);
                listOfLobbies.Add(tempLobbyID);
            }
            MelonLogger.Msg(listOfLobbies.Count + " Lobbies Found");

            if(lobbyName == "Random" && listOfLobbies.Count > 0)
            {
                foreach(var lobby in listOfLobbies)
                {
                    MelonLogger.Msg("Joining Lobby");
                    SteamMatchmaking.JoinLobby(lobby);
                    return;
                }
            }
        }
        private void OnLobbyEnter(LobbyEnter_t callback, bool bIOFailure)
        {
            MelonLogger.Msg("Joined Lobby");
            if(callback.m_EChatRoomEnterResponse != 1)
            {
                MelonLogger.Msg("Failed To Enter Lobby: " + (uint)callback.m_EChatRoomEnterResponse);
                return;
            }
            lobbyID = (CSteamID)callback.m_ulSteamIDLobby;
            lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "Joined Lobby!";

        }
        #endregion
    }
}