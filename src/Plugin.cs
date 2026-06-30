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
using nickeltin.SDF.Runtime;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(CWMultiplayer.MultiplayerManager), "Multiplayer Mod", "0.1.0", "Purely_K2")]
[assembly: MelonGame("Buried Things", "Cursed Words")]


/// To do:
/// 1. Fix Nat's Interactions With Inventory Visuals
/// 2. Track ReceivedInfo.foeBoss instead of foeCharacter
/// 
/// OPTIONAL
/// 3. Add wait before starting boss battle?
/// 4. Make Time Limit From One Boss To The Next (override speedrun timer to do so?)


namespace CWMultiplayer
{
    public class MultiplayerManager : MelonMod
    {
        public static bool debugMode = true;
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
            if(debugMode) MelonLogger.Msg("Loaded Multiplayer Mod");
        }
        public override void OnApplicationQuit()
        {
            CursedUI.DisableCallbacks();
            if(CursedUI.lobbyName != "")
            {
                SteamMatchmaking.LeaveLobby(CursedUI.lobbyID);
                if(debugMode) MelonLogger.Msg("Disconnected from lobby");
            }
            if(debugMode) MelonLogger.Msg("Shut Down Multiplayer Mod");
        }
        #endregion

        #region Bosses Stuff
        [HarmonyPatch(typeof(BossDraftController), "Start")]
        public static class AutoChooseBoss_Patch
        {
            public static void Postfix(ref BossDraftController __instance, ref BossDraftVisualController ____visualController)
            {
                if(!ReceivedInfo.hasOpponent) return;

                ____visualController.Select(true);
                __instance.BossSelectButtonCallback();
            }
        }
        [HarmonyPatch(typeof(EncounterController), "GenerateGrid", new System.Type[] {typeof(bool)})]
        public static class ApplyBossModifier_Patch //inBoss, total target = 1
        {
            public static void Prefix(ref List<BossModifier> ____bossModifiers)
            {
                if(!ReceivedInfo.hasOpponent) return;
                try
                {
                    if(____bossModifiers.Count > 0)
                    {
                        CursedNetworking.myPlayerPacket.UpdatePacket(true, CursedNetworking.myPlayerPacket.highScore, CursedNetworking.myPlayerPacket.health);
                    }
                    else
                    {
                        CursedNetworking.myPlayerPacket.inBoss = false;
                    }

                    if(CursedNetworking.myPlayerPacket.inBoss && ReceivedInfo.hasOpponent)
                    {
                        if(ReceivedInfo.opponentHighscore.Score > 0)
                            encounterController.SetTotalTarget((int)ReceivedInfo.opponentHighscore.Score);
                        else
                            encounterController.SetTotalTarget(1);
                    }
                }
                catch(System.Exception e)
                {
                    MelonLogger.Msg(e);
                }
            }
        }
        [HarmonyPatch(typeof(EncounterController), "Start")]
        public static class Encounter_Start_Patch //inBoss, total target = 1, BossModifierFloorAdjusting
        {
            public static void Prefix(ref List<BossModifier> ____bossModifiers, ref EncounterController __instance, ref EncounterSummaryDisplayController ____encounterSummaryDisplayController)
            {
                if(!ReceivedInfo.hasOpponent)
                {
                    return;
                }

                //Always Do Things
                CursedUI.UpdateHearts();

                encounterController = __instance;
                encounterSummaryDisplayController = ____encounterSummaryDisplayController;

                if(____bossModifiers.Count > 0)
                {
                    CursedNetworking.myPlayerPacket.inBoss = true;
                }
                else CursedNetworking.myPlayerPacket.highScore = new ScorePacket(0);

                if(CursedNetworking.myPlayerPacket.inBoss && ReceivedInfo.hasOpponent)
                {
                    if(ReceivedInfo.opponentHighscore.Score <= 0)
                        encounterController.SetTotalTarget(1);
                    else
                        encounterController.SetTotalTarget((int)ReceivedInfo.opponentHighscore.Score);
                }
            }
        }
        [HarmonyPatch(typeof(EncounterController), "SubmitWord", new System.Type[] { typeof(List<TileSelection>), typeof(List<string>) })]
        public static class SubmitWord_Patch
        {
            private static async Task AsyncronousWaiting(EncounterController encounterController, List<TileSelection> tiles, List<string> words)
            {
                await Task.Delay(1);
                encounterController.SubmitWord(tiles, words);
            }
            public static bool Prefix(ref int ____remainingGrids, ref EncounterController __instance, ref List<TileSelection> tiles, ref List<string> words, ref GridData ____gridData, ref List<HistoricWord> ____previousWords)
            {
                BonesBoss bones = new BonesBoss();
                bones.SetFloorAdjustedModification(GameStatics.GetPlayer().CurrentRunProgress.GetStage() - 1, false);
                BonesBoss.wordScoreTaken += bones.FloorAdjustedModification * tiles.Count;
                
                if(!ReceivedInfo.hasOpponent || GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType != NodeType.Boss) return true;

                try
                {
		            __instance.SetEncounterThreadStage(EncounterThreadStage.ExecutingWordConsequences);
                    CursedUI.waitingTextObj.SetActive(true);
                    CursedUI.overrideWaitingButtonObj.SetActive(true);

                    if(____remainingGrids <= 0 && ReceivedInfo.hasOpponent)
                    {
                        if(CursedNetworking.myPlayerPacket.inBoss)
                        {
                            if(debugMode) MelonLogger.Msg("Finished Battle!");
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
                            if(scorePacket > CursedNetworking.myPlayerPacket.highScore)
                            {
                                CursedNetworking.myPlayerPacket.UpdatePacket(false, scorePacket, CursedNetworking.myPlayerPacket.health);
                            }
                            else
                            {
                                CursedNetworking.myPlayerPacket.UpdatePacket(false, CursedNetworking.myPlayerPacket.highScore, CursedNetworking.myPlayerPacket.health);
                            }
                        }
                        //Freezes Game Until Opponent Gets There (If was in boss)
                        if(ReceivedInfo.opponentHealth > 0 && CursedNetworking.myPlayerPacket.health > 0 && (ReceivedInfo.opponentHighscore.Score == 0 || ReceivedInfo.opponentIsInBoss) && CursedNetworking.myPlayerPacket.highScore.Score > 0)
                        {
                            _ = AsyncronousWaiting(__instance, tiles, words);
                            return false;
                        }
                        else
                        {
                            if(debugMode) MelonLogger.Msg("Opponent Is Done, continuing...");
                        }
                    }
                }
                catch (System.Exception e)
                {
                    MelonLogger.Msg(e);
                }
                CursedUI.overrideWaitingButtonObj.SetActive(false);
                CursedUI.waitingTextObj.SetActive(false);

                return true;
            }
            public static void Postfix(ref ScorePacket ____remainingTarget, ref int ____totalGridsPerRound, ref int ____remainingGrids, ref EncounterSummaryDisplayController ____encounterSummaryDisplayController)
            {
                if(!ReceivedInfo.hasOpponent || GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType != NodeType.Boss) return;

                try
                {
                    if (CursedNetworking.myPlayerPacket.inBoss && ____remainingGrids > 0 && ReceivedInfo.hasOpponent)
                    {
                        if(ReceivedInfo.opponentHighscore.Score > 0)
                            ____remainingTarget = new ScorePacket(mostRecentScorePacket.Score + ReceivedInfo.opponentHighscore.Score);
                        else ____remainingTarget = new ScorePacket(mostRecentScorePacket.Score + 1);
                        if(mostRecentScorePacket > CursedNetworking.myPlayerPacket.highScore) CursedNetworking.myPlayerPacket.UpdatePacket(true, mostRecentScorePacket, CursedNetworking.myPlayerPacket.health);
                        else if(debugMode) MelonLogger.Msg("Not Highest Score");
                    }
                    else if(CursedNetworking.myPlayerPacket.highScore.Score > 0 && ReceivedInfo.opponentHighscore.Score > 0 && !ReceivedInfo.opponentIsInBoss && !CursedNetworking.myPlayerPacket.inBoss)
                    {
                        if(ReceivedInfo.opponentHighscore > CursedNetworking.myPlayerPacket.highScore)
                        {
                            CursedNetworking.myPlayerPacket.UpdatePacket(CursedNetworking.myPlayerPacket.inBoss, CursedNetworking.myPlayerPacket.highScore, CursedNetworking.myPlayerPacket.health - 1);
                            if(debugMode) MelonLogger.Msg("You Lost A Life!\nCurrent Life: " + CursedNetworking.myPlayerPacket.health);
                        }
                        else if(ReceivedInfo.opponentHighscore == CursedNetworking.myPlayerPacket.highScore)
                        {
                            if(debugMode) MelonLogger.Msg("You Tied And Both Lose A Life!");
                        }
                        else
                        {
                            if(debugMode) MelonLogger.Msg("You Won The Floor!");
                        }

                        if(CursedNetworking.myPlayerPacket.health > 0)
                        {
                            ____remainingTarget = new ScorePacket(-1);
                            
                            //Boss Money
                            Player player = GameStatics.GetPlayer();
                            int gridsForMoney = ____totalGridsPerRound - 1;

                            player.ChangeMoney(gridsForMoney * 2);
                        }
                        else
                        {
                            if(debugMode) MelonLogger.Msg("Game Over! You lose!");
                            ____remainingTarget = new ScorePacket(mostRecentScorePacket.Score + ReceivedInfo.opponentHighscore.Score);
                        }

                        ReceivedInfo.opponentHighscore = new ScorePacket(0);
                        _ = AsyncReset();
                    }

                    currentRemainingTarget = ____remainingTarget;

                    ____encounterSummaryDisplayController.ShowBoss(encounterController.GetBossModifiers()[0]);
                }
                catch (System.Exception e)
                {
                    MelonLogger.Msg(e);
                }
            }
            private static async Task AsyncReset()
            {
                await Task.Delay(10000);
                
                if(CursedNetworking.myPlayerPacket.health <= 0)
                {
                    ReceivedInfo.ResetInfo();
                    CursedNetworking.myPlayerPacket.ResetPacket();
                }
                CursedNetworking.myPlayerPacket.UpdatePacket(false, new ScorePacket(0), CursedNetworking.myPlayerPacket.health);
            }
        }
        //Skip to the end if you won
        [HarmonyPatch(typeof(RunProgress), "IsFinalStage")]
        public static class Winning_Patch
        {
            public static void Postfix(ref bool __result)
            {
                if(ReceivedInfo.hasOpponent && CursedNetworking.myPlayerPacket.highScore.Score > 0)
                {
                    if(CursedNetworking.myPlayerPacket.highScore > ReceivedInfo.opponentHighscore && ReceivedInfo.opponentHealth <= 0)
                    {
                        __result = true;
                        if(debugMode) MelonLogger.Msg("YOU WON! (Tell K2 If The Victory Screen Didn't Show Up)");
                    }
                }
            }
        }
        //Boss Reward Increase To Make Up For Grids
        [HarmonyPatch(typeof(Reward), MethodType.Constructor, new System.Type[] { typeof(string), typeof(int), typeof(bool), typeof(bool) })]
        public static class IncreaseBossReward_Patch
        {
            public static void Prefix(ref string rewardDescription, ref int rewardCashAmount)
            {
                if(!ReceivedInfo.hasOpponent) return;

                if(rewardDescription == "Boss defeated!" && rewardCashAmount == 5)
                {
                    if(debugMode) MelonLogger.Msg("Increasing Boss Payout");
                    rewardCashAmount += GameStatics.GetPlayer().CurrentRunProgress.Ascension == AscensionLevel.OneFewerGrid ? 6 : 8;
                }
                else if(rewardDescription == "Boss defeated!")
                {
                    if(debugMode) MelonLogger.Msg("Must've Missed Somethin'");
                }
            }
        }
        //Patch Skipping Last Grid
        [HarmonyPatch(typeof(EncounterController), "SkipWordSubmission")]
        public static class IsStillWin_Patch
        {
            public static bool Prefix(ref int ____remainingGrids, ref EncounterController __instance)
            {
                if(____remainingGrids > 0 || !ReceivedInfo.hasOpponent) return true;

                __instance.SubmitWord(new List<TileSelection>(), new List<string> { "!!!" });
                return false;
            }
        }
        #endregion

        #region Sprite Overrides
        [HarmonyPatch(typeof(CharacterSelectController), "TransitionToNextScene")]
        public static class GetActiveCharacter_Patch
        {
            private static async Task AsyncronousWaiting(CharacterSelectController characterSelectController)
            {
                await Task.Delay(1);
                AccessTools.Method(typeof(CharacterSelectController), "TransitionToNextScene").Invoke(characterSelectController, new object[] {});
            }
            public static bool Prefix(ref Character ____activeCharacter, ref CharacterSelectController __instance)
            {
                if(!ReceivedInfo.hasOpponent) return true;

                try
                {
                    CursedNetworking.myPlayerPacket.myCharacterName = ____activeCharacter.GetName();
                    CursedNetworking.myPlayerPacket.UpdatePacket(false, new ScorePacket(0), 3);
                    if(debugMode) MelonLogger.Msg("Updated Player Character to: " + ____activeCharacter.GetName());
                    BonesBoss.wordScoreTaken = 0;

                    if(ReceivedInfo.foeCharacter == null)
                    {
                        CursedUI.waitingTextObj.SetActive(true);
                        CursedUI.menuButtonCoverObj.SetActive(true);
                        _ = AsyncronousWaiting(__instance);
                        return false;
                    }
                    
                    CursedUI.waitingTextObj.SetActive(false);
                    CursedUI.menuButtonCoverObj.SetActive(false);
                }
                catch (System.Exception e)
                {
                    MelonLogger.Msg(e);
                }

                return true;
            }
        }
        //Boss Draft Stuff
        [HarmonyPatch(typeof(RunProgress), "GetCurrentBossDraft")]
        public static class BossDraft_Patch
        {
            public static void Postfix(ref BossDraft __result)
            {
                if(!ReceivedInfo.hasOpponent) return;
                try
                {
                    __result.AvailableBosses.RemoveAll(t => true);

                    BossModifier bossModifier = new RandomiseItemOrder();
                    if(ReceivedInfo.foeCharacter != null)
                    {   
                        System.Type foeCharacterType = ReceivedInfo.foeCharacter.GetType();

                        if(foeCharacterType == typeof(WetDennis))
                            bossModifier = new RodmanBoss();
                        else if(foeCharacterType == typeof(NinaNix))
                            bossModifier = new NinaNixBoss();
                        else if(foeCharacterType == typeof(HayleyBayles))
                            bossModifier = new HayleyBaylesBoss();
                        else if(foeCharacterType == typeof(SamGambit))
                            bossModifier = new SamGambitBoss();
                        else if(foeCharacterType == typeof(BonesTheDog))
                            bossModifier = new BonesBoss();
                        else if(foeCharacterType == typeof(Octacles))
                            bossModifier = new OctaclesBoss();
                        else if(foeCharacterType == typeof(NathaServo))
                            bossModifier = new NatBoss();
                        else if(foeCharacterType == typeof(SandySaguaro))
                            bossModifier = new SandySaguaroBoss();
                        else if(foeCharacterType == typeof(Spike))
                            bossModifier = new CretaceousMegBoss();
                        else if(foeCharacterType == typeof(SockHead))
                            bossModifier = new HumanBoyBoss();
                        else if(foeCharacterType == typeof(PrismaticBean))
                            bossModifier = new PrismaticBeanBoss();
                        else if(debugMode) MelonLogger.Msg("Option Is An Invalid Character");
                    }

                    if(bossModifier.GetType() != typeof(HumanBoyBoss))
                        bossModifier.SetFloorAdjustedModification(GameStatics.GetPlayer().CurrentRunProgress.GetStage(), false);

                    __result.AvailableBosses.Add(bossModifier);
                    __result.AvailableBosses.Add(bossModifier);
                }
                catch (System.Exception e)
                {
                    MelonLogger.Msg(e);
                }
            }
        }
        
        //BossCharacter Shows Up In Non-Bosses
        [HarmonyPatch(typeof(EncounterSummaryDisplayController), "SetInitialDisplayedTargetValue", new System.Type[] { typeof(ScorePacket) })]
        public static class OlWesternStareDown_Patch
        {
            public static void Prefix(ref TextMeshProUGUI ____bossModTMP, ref EncounterSummaryDisplayController __instance)
            {
                if(!ReceivedInfo.hasOpponent || ReceivedInfo.foeCharacter == null || GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType == NodeType.Boss) return;

                if(debugMode) MelonLogger.Msg("Showing Foe Character");

                System.Type foeCharacterType = ReceivedInfo.foeCharacter.GetType();
                BossModifier bossModifier = new RandomiseItemOrder();

                if(foeCharacterType == typeof(WetDennis))
                    bossModifier = new RodmanBoss();
                else if(foeCharacterType == typeof(NinaNix))
                    bossModifier = new NinaNixBoss();
                else if(foeCharacterType == typeof(HayleyBayles))
                    bossModifier = new HayleyBaylesBoss();
                else if(foeCharacterType == typeof(SamGambit))
                    bossModifier = new SamGambitBoss();
                else if(foeCharacterType == typeof(BonesTheDog))
                    bossModifier = new BonesBoss();
                else if(foeCharacterType == typeof(Octacles))
                    bossModifier = new OctaclesBoss();
                else if(foeCharacterType == typeof(NathaServo))
                    bossModifier = new NatBoss();
                else if(foeCharacterType == typeof(SandySaguaro))
                    bossModifier = new SandySaguaroBoss();
                else if(foeCharacterType == typeof(Spike))
                    bossModifier = new CretaceousMegBoss();
                else if(foeCharacterType == typeof(SockHead))
                    bossModifier = new HumanBoyBoss();
                else if(foeCharacterType == typeof(PrismaticBean))
                    bossModifier = new PrismaticBeanBoss();
                else if(debugMode) MelonLogger.Msg("Option Is An Invalid Character");

                __instance.ShowBoss(bossModifier);
                ____bossModTMP.SetText("Get to the next Boss to battle!");
            }
        }

        //Skip Dialogue
        [HarmonyPatch(typeof(BossDraftController), "ShowSecretBossIntroDialogue")]
        public static class StopDialogue_Patch
        {
            public static bool Prefix()
            {
                return !ReceivedInfo.hasOpponent;
            }
        }
        [HarmonyPatch(typeof(HumanBoyBoss), "GetSecretBossDefeatDialogue")]
        public static class PutASockInIt_Patch
        {
            public static void Postfix(ref List<DiscussionPacket> __result, ref HumanBoyBoss __instance)
            {
                if(!ReceivedInfo.hasOpponent) return;

                __result = new List<DiscussionPacket>();
            }
        }
        [HarmonyPatch(typeof(CretaceousMegBoss), "GetSecretBossDefeatDialogue")]
        public static class HeardItBefore_Patch
        {
            public static void Postfix(ref List<DiscussionPacket> __result)
            {
                __result = new List<DiscussionPacket>();
            }
        }
        [HarmonyPatch(typeof(SandySaguaroBoss), "GetSecretBossDefeatDialogue")]
        public static class QuietLikeTheWind_Patch
        {
            public static void Postfix(ref List<DiscussionPacket> __result)
            {
                __result = new List<DiscussionPacket>();
            }
        }
        [HarmonyPatch(typeof(PrismaticBeanBoss), "GetSecretBossDefeatDialogue")]
        public static class DontCare_Patch
        {
            public static void Postfix(ref List<DiscussionPacket> __result)
            {
                __result = new List<DiscussionPacket>();
            }
        }
        
        //Stop Meg Shop
        [HarmonyPatch(typeof(RunProgress), "GoToNextNodeAndGetSceneName")]
        public static class DenyShoppingTrip_Patch
        {
            public static void Postfix(ref RunProgress __instance, ref string __result)
            {
                if(__instance.CurrentNodeType == NodeType.MegShop && ReceivedInfo.hasOpponent)
                {
                    __instance.CurrentNodeType = NodeType.Boss;
                    __result = SceneNames.EncounterSceneName;
                    (GameStatics.GetPlayer().ActiveBossModifiers.First((BossModifier boss) => boss is CretaceousMegBoss) as CretaceousMegBoss).RestorePlayerInventory();
                }
            }
        }
        
        //Flip Sprite
        [HarmonyPatch(typeof(EnemyVisualController), "PopulateEnemyAnimator")]
        public static class FlipBosses_Patch
        {
            public static void Postfix(ref Animator ____portraitAnimator)
            {
                if(ReceivedInfo.foeCharacter != null && !new List<System.Type> {typeof(SandySaguaro), typeof(Spike), typeof(SockHead), typeof(PrismaticBean)}.Contains(ReceivedInfo.foeCharacter.GetType()))
                {
                    RectTransform rect = ____portraitAnimator.transform.parent.GetComponent<RectTransform>();
                    rect.localScale = new Vector3(-1, 1, 1);
                    rect.localPosition = new Vector3(200, rect.localPosition.y, rect.localPosition.z);
                }
                else
                    ____portraitAnimator.gameObject.GetComponent<RectTransform>().localScale = Vector3.one;
            }
        }
        #endregion

        #region My UI Activation
        [HarmonyPatch(typeof(ShopController), "Start")]
        public static class ShopControllerStart_Patch
        {
            public static void Postfix()
            {
                CursedUI.overrideWaitingButtonObj.SetActive(false);
                CursedUI.waitingTextObj.SetActive(false);
                CursedUI.showLobbyButtonObj.SetActive(true);
            }
        }
        [HarmonyPatch(typeof(EncounterController), "Start")]
        public static class EncounterControllerStart_Patch
        {
            public static void Postfix()
            {
                CursedUI.showLobbyButtonObj.SetActive(false);
            }
        }

        //Button And Such Appearances
        [HarmonyPatch(typeof(SaveSlotController), "Awake")]
        public static class LobbyUISetup_Patch
        {
            static void Postfix(SaveSlotController __instance)
            {
                if(CursedUI.showLobbyButtonObj.GetComponentsInChildren<Component>().ToList().Exists(component => component.GetType() == typeof(EventTrigger))) return;
                try
                {
                    MakeAnimatedButton(CursedUI.showLobbyButtonObj);
                    MakeAnimatedButton(CursedUI.hideLobbyButtonObj);
                    MakeAnimatedButton(CursedUI.backButtonObj);
                    MakeAnimatedButton(CursedUI.hostButtonObj);
                    MakeAnimatedButton(CursedUI.lobbyButtonObj);
                    MakeAnimatedButton(CursedUI.joinLobbyButtonObj);
                    FixOtherUISTuff(CursedUI.canvasObj);
                }
                catch(System.Exception e)
                {
                    MelonLogger.Msg(e);
                }
            }

            public static void MakeAnimatedButton(GameObject baseButton)
            {
                Canvas canvas = Object.FindFirstObjectByType<Canvas>();
                Button[] selectButtons = Object.FindObjectsOfType<Button>().Where(button => button.GetComponentInChildren<TextMeshProUGUI>().text == "SELECT").ToArray<Button>();
                GameObject selectButton = selectButtons[selectButtons.Count() - 1].transform.parent.gameObject;
                GameObject selectButtonTop = selectButton.GetComponentInChildren<Button>().gameObject;

                Vector2 lobbyButtonSize = baseButton.GetComponent<RectTransform>().sizeDelta;
                baseButton.GetComponentInChildren<TextMeshProUGUI>().color = Color.black;
                baseButton.GetComponent<Image>().enabled = false;
                baseButton.GetComponentInChildren<TextMeshProUGUI>().raycastTarget = false;
                GameObject thisButtonTop = new GameObject("ButtonTop", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(EventTrigger) });
                thisButtonTop.transform.SetParent(baseButton.transform);
                thisButtonTop.GetComponent<RectTransform>().localPosition = Vector3.zero;
                thisButtonTop.GetComponent<RectTransform>().sizeDelta = new Vector2(lobbyButtonSize.x, lobbyButtonSize.y * 5 / 6);
                thisButtonTop.GetComponent<Image>().sprite = selectButtonTop.GetComponent<Image>().sprite;
                thisButtonTop.GetComponent<Image>().color = selectButtonTop.GetComponent<Image>().color;
                thisButtonTop.GetComponent<Button>().onClick = baseButton.GetComponent<Button>().onClick;
                thisButtonTop.GetComponent<Button>().transition = Selectable.Transition.Animation;

                //Button Pressed
                EventTrigger.Entry pointerDownEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                pointerDownEntry.callback.AddListener((data) =>
                {
		            thisButtonTop.GetComponent<RectTransform>().localPosition = -6f * Vector2.up;
		            PersistentSound.SingletonSoundController.ButtonPress();
                });
                thisButtonTop.GetComponent<EventTrigger>().triggers.Add(pointerDownEntry);

                //Button Released
                EventTrigger.Entry pointerUpEntry = new EventTrigger.Entry();
                pointerUpEntry.eventID = EventTriggerType.PointerUp;
                pointerUpEntry.callback.AddListener((data) =>
                {
                    if(baseButton.name.Contains("Lobby Button") && baseButton.name != "Lobby Button")
                    {
                        CursedUI.isUIOpen = baseButton.name == "Show Lobby Button";
                    }
		            thisButtonTop.GetComponent<RectTransform>().localPosition = Vector2.zero;
		            PersistentSound.SingletonSoundController.ButtonRelease();
                });
                thisButtonTop.GetComponent<EventTrigger>().triggers.Add(pointerUpEntry);
                
                
                TextMeshProUGUI thisText = baseButton.GetComponentInChildren<TextMeshProUGUI>();
                thisText.transform.SetParent(thisButtonTop.transform);
                thisText.font = selectButton.GetComponentInChildren<TextMeshProUGUI>().font;
                thisText.fontWeight = FontWeight.ExtraLight;
                thisText.color = selectButton.GetComponentInChildren<TextMeshProUGUI>().color;
                thisText.autoSizeTextContainer = true;

                GameObject thisButtonBG = new GameObject("ButtonTop", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(Image) });
                thisButtonBG.transform.SetParent(baseButton.transform);
                thisButtonBG.GetComponent<RectTransform>().localPosition = Vector3.down * 16f;
                thisButtonBG.GetComponent<RectTransform>().sizeDelta = new Vector2(lobbyButtonSize.x, lobbyButtonSize.y * 5 / 6);
                thisButtonBG.GetComponent<Image>().sprite = selectButtonTop.GetComponent<Image>().sprite;
                thisButtonBG.GetComponent<Image>().color = selectButtonTop.GetComponent<Image>().color;
                thisButtonBG.GetComponent<Image>().color = selectButton.GetComponent<Image>().color;

                thisButtonTop.transform.SetAsFirstSibling();
                thisButtonBG.transform.SetAsFirstSibling();

                if(baseButton.name.Contains("Lobby Button") && (baseButton.name.Contains("Show") || baseButton.name.Contains("Hide")))
                {
                    baseButton.GetComponent<RectTransform>().localPosition = new Vector3(-1 * Screen.width * 3 / 8, Screen.height * 13 / 32, 0);
                    baseButton.GetComponent<RectTransform>().localScale = new Vector2(0.75f, 0.75f);
                }
            }
            public static void FixOtherUISTuff(GameObject baseThingy)
            {
                //Basics
                Canvas canvas = Object.FindFirstObjectByType<Canvas>();
                Button[] selectButtons = Object.FindObjectsOfType<Button>().Where(button => button.GetComponentInChildren<TextMeshProUGUI>().text == "SELECT").ToArray();
                GameObject selectButton = selectButtons[selectButtons.Count() - 1].transform.parent.gameObject;

                //Font
                TextMeshProUGUI[] areaTexts = baseThingy.GetComponentsInChildren<TextMeshProUGUI>();
                foreach (TextMeshProUGUI areaText in areaTexts)
                {
                    areaText.font = selectButton.GetComponentInChildren<TextMeshProUGUI>().font;
                    areaText.color = selectButton.GetComponentInChildren<TextMeshProUGUI>().color;
                }
            }
        }
        #endregion

        #region Round Stuff
        //Make a Challenge
        [HarmonyPatch(typeof(EncounterController), "Start")]
        public static class IsThatAChallengeIHear_Patch
        {
            public static void Prefix()
            {
                if(GameStatics.GetPlayer().CurrentRunProgress.Challenge == null && ReceivedInfo.hasOpponent)
                    GameStatics.GetPlayer().CurrentRunProgress.Challenge = new Multiplayer();
            }
        }
        //Get Score For Word
        [HarmonyPatch(typeof(ScoreCalculation), "GetScoreFromScoreCalcInfo", new System.Type[] { typeof(List<ScoreCalcVizInfo>) })]
        public static class GetScoreFromScoreCalcInfo_Patch
        {
            public static void Postfix(ref ScorePacket __result)
            {
                mostRecentScorePacket = __result;
            }
        }
        //Still win a round if you lose
        [HarmonyPatch(typeof(EncounterController), "TryGoToNextGrid")]
        public static class WinAndLose_Patch
        {
            public static void Prefix(ref ScorePacket ____remainingTarget, ref int ____remainingGrids)
            {
                if(____remainingGrids <= 0 && GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType != NodeType.Boss)
                {
                    if(____remainingTarget > new ScorePacket(0L) && CursedNetworking.myPlayerPacket.health > 1)
                    {
                        ____remainingTarget = new ScorePacket(-1);

                        if(debugMode) MelonLogger.Msg("Welp... You Lost A Life!");
                        CursedNetworking.myPlayerPacket.UpdatePacket(CursedNetworking.myPlayerPacket.inBoss, CursedNetworking.myPlayerPacket.highScore, CursedNetworking.myPlayerPacket.health - 1);
                    }
                }
            }
        }
        #endregion
        
        #region Item Balancing
        //Remove Weird Items From Pools
        [HarmonyPatch(typeof(ItemPools), "PopulatePools")]
        public static class PopulatePools_Patch
        {
            public static void Postfix(ref List<System.Type> ____stickerPool, ref List<System.Type> ____stampPool)
            {
                ____stickerPool.RemoveAll(item => item == typeof(DivingMask));
                ____stampPool.RemoveAll(item => item == typeof(BlessingOfTheFairies) || item == typeof(Receipt));
            }
        }
        //Nerf Cursed VHS
            #region Cursed VHS
            [HarmonyPatch(typeof(CursedVHS), MethodType.Constructor)]
            public static class CursedVHS_Patch
            {
                public static void Postfix(ref List<UpgradeableComponent> ___UpgradeableComponents, ref ItemRarity ___Rarity, ref int ___Cost, ref List<ItemTag> ___Tags, ref List<TileType> ___RelevantColours)
                {
                    ___UpgradeableComponents = new List<UpgradeableComponent>();
                    ___Rarity = ItemRarity.Rare;
                }
            }
                //Fix Description
                [HarmonyPatch(typeof(CursedVHS), "GetDescription")]
                public static class CursedVHS_Description_Patch
                {
                    public static bool Prefix(ref string __result)
                    {
                        __result = "START OF GRID: Scatters a level 1 item from the cursed item pool for each curse type on the grid";
                        return false;
                    }
                }
                //Fix Effect
                [HarmonyPatch(typeof(CursedVHS), "ApplyStartOfGridEffect", new System.Type[] { typeof(GridData), typeof(int), typeof(int), typeof(List<HistoricWord>), typeof(List<BoardGenVizInfo>), typeof(bool) })]
                public static class CursedVHS_Effect_Patch
                {
                    public static bool Prefix(ref GridData __result, ref GridData gridData, ref List<BoardGenVizInfo> vizSteps, ref CursedVHS __instance)
                    {
                        List<Tile> list = new List<Tile>();
                        List<Tile> list2 = new List<Tile>();
                        List<CurseType> list3 = new List<CurseType>();
                        foreach (Tile tile in gridData.GetAvailableTiles())
                        {
                            foreach (CurseType curseType in tile.GetCurseTypes())
                            {
                                if (!list3.Contains(curseType) && curseType != CurseType.None)
                                {
                                    list2.Add(tile);
                                    list3.Add(curseType);
                                }
                            }
                        }
                        for (int i = 0; i < list3.Count; i++)
                        {
                            Tile tileForItemScatter = GridUtility.Singleton.GetTileForItemScatter(gridData, TileType.Normal, GlyphType.ScatteredItem, null, false);
                            if (tileForItemScatter != null)
                            {
                                tileForItemScatter.SetScatteredItem(ScatteredItemPools.GetRandomCursedBuildItem());
                                list.Add(tileForItemScatter);
                            }
                        }
                        if (list.Count > 0)
                        {
                            vizSteps.Add(new BoardGenVizInfo(gridData, __instance, list, false, null, true, false, false, vizSteps[vizSteps.Count - 1].PlayerConsumableTiles));
                        }
                        __result = gridData;

                        return false;
                    }
                }
            #endregion
        #endregion

        #region Other Stuff
        //Remove Puzzle Button
        [HarmonyPatch(typeof(SaveManager), "HasAcquiredAllFairies")]
        public static class NoPuzzlesForYou_Patch
        {
            public static void Postfix(ref bool __result)
            {
                if(ReceivedInfo.hasOpponent) __result = false;
            }
        }

        //Remove Continue Button
        [HarmonyPatch(typeof(SaveManager), "GetCurrentRun")]
        public static class NoContinuesForYou_Patch
        {
            public static void Postfix(ref Player __result)
            {
                if(CursedUI.lobbyID != CSteamID.Nil && SceneManager.GetActiveScene().name == SceneNames.MainMenuSceneName)
                {
                    __result = null;
                }
            }
        }

        //Other
        private static Vector2 resolution = new Vector2(Screen.width, Screen.height);
        public override void OnUpdate()
        {
            base.OnUpdate();
            if(CursedNetworking.myPlayerPacket.highScore.Score > 0 && ReceivedInfo.opponentHighscore.Score > 0 && !ReceivedInfo.delayScoreUpdates)
            {
                encounterSummaryDisplayController.UpdateDisplayedTargetValue(ReceivedInfo.opponentHighscore, ReceivedInfo.opponentHighscore, false);
            }
            if(SteamAPI.Init()) SteamAPI.RunCallbacks();
            if(CursedUI.lobbyID != CSteamID.Nil) CursedNetworking.UpdateAndSendPlayerPacket();
            if(SteamMatchmaking.GetNumLobbyMembers(CursedUI.lobbyID) > 1 && !ReceivedInfo.hasOpponent)
            {
                ReceivedInfo.hasOpponent = true;
                if(debugMode) MelonLogger.Msg("2 People In Lobby!");
            }
            if(SteamMatchmaking.GetNumLobbyMembers(CursedUI.lobbyID) == 1 && ReceivedInfo.hasOpponent)
            {
                ReceivedInfo.hasOpponent = false;
                if(debugMode) MelonLogger.Msg("Opponent Disconnected");
                ReceivedInfo.ResetInfo();
                CursedNetworking.myPlayerPacket.ResetPacket();
                CursedNetworking.myPlayerPacket.playerName = "Player 1";
                CursedNetworking.isHost = true;
            }

            //Resolution Stuff
            if(resolution.x != Screen.width || resolution.y != Screen.height)
            {
                resolution.x = Screen.width;
                resolution.y = Screen.height;
                CursedUI.SetUpUIAppearance();
            }

            //Overlay Stuff
            CursedUI.ToggleOverlay(SceneManager.GetActiveScene().name == SceneNames.EncounterSceneName && ReceivedInfo.hasOpponent);
            if(CursedUI.showLobbyButtonObj.activeSelf == CursedUI.hideLobbyButtonObj.activeSelf)
            {
                if(!CursedUI.showLobbyButtonObj.activeSelf)
                {
                    CursedUI.CloseLobbyStuff();
                    CursedUI.isUIOpen = false;
                }
                else
                {
                    CursedUI.isUIOpen = true;
                }
            }
            CursedUI.showLobbyButtonObj.SetActive((SceneManager.GetActiveScene().name == SceneNames.SaveSlotsScene || (ReceivedInfo.hasOpponent && SceneManager.GetActiveScene().name != SceneNames.EncounterSceneName)) && SceneManager.GetActiveScene().name != "PreRoll" && !CursedUI.isUIOpen);

            if(!new string[] { SceneNames.EncounterSceneName, SceneNames.ShopSceneName, SceneNames.BossDraftSceneName, SceneNames.BossRewardSceneName }.Contains(SceneManager.GetActiveScene().name))
            {
                CursedNetworking.myPlayerPacket.health = 3;
                CursedNetworking.myPlayerPacket.highScore = new ScorePacket(0);
                CursedNetworking.myPlayerPacket.inBoss = false;

                ReceivedInfo.opponentHealth = 3;
                ReceivedInfo.opponentHighscore = new ScorePacket(0);
                ReceivedInfo.opponentIsInBoss = false;
            }

            CursedUI.bossEffectsToggleObj.SetActive(CursedNetworking.myPlayerPacket.playerName == "Player 1" || CursedNetworking.myPlayerPacket.playerName == "");
            CursedUI.bossEffectsToggleObj.GetComponent<Image>().color = ReceivedInfo.noBossEffects ? new UnityEngine.Color(1, 1, 1, 0.1f) : Color.white;
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

        #region Boss Effects
        //Start And End
        private static bool isDowngraded = false;
        [HarmonyPatch(typeof(EncounterController), "SetTotalTarget", new System.Type[] { typeof(int) })]
        public static class HumanHitsYou_Patch
        {
            public static void Postfix()
            {
                if(ReceivedInfo.hasOpponent && encounterController != null && GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType == NodeType.Boss && encounterController.GetBossModifiers().Select(m => m.GetType()).Contains(typeof(HumanBoyBoss)) && !ReceivedInfo.noBossEffects)
                {
                    if(isDowngraded) return;

                    if(debugMode) MelonLogger.Msg("Vs Human Boy");
                    
                    isDowngraded = true;

                    foreach (Item item in GameStatics.GetPlayer().GetAllItems())
                    {
                        if(item.UpgradeableComponents.Count == 1 && item.UpgradeableComponents[0].Level > 1)
                        {
                            item.Downgrade(0);
                        }
                    }
                }
			    CharacterInfoPanel.SingletonInventoryVisualController.PopulateStickers();
            }
        }
        [HarmonyPatch(typeof(PinDraftVisualController), "Populate")]
        public static class YouHitHimBack_Patch
        {
            public static void Prefix()
            {
                if(isDowngraded)
                {
                    isDowngraded = false;

                    foreach (Item item in GameStatics.GetPlayer().GetAllItems())
                    {
                        if(item.UpgradeableComponents.Count == 1)
                        {
                            item.Upgrade(0);
                        }
                    }
                }
			    CharacterInfoPanel.SingletonInventoryVisualController.PopulateAll();
            }
        }
        //Grid
        [HarmonyPatch(typeof(EncounterController), "ShowGridGenerationViz", new System.Type[] { typeof(List<BoardGenVizInfo>) })]
        public static class EnsureItGetsDoneFirst_Patch
        {
            public static void Prefix(ref EncounterSummaryDisplayController ____encounterSummaryDisplayController)
            {
                if(!ReceivedInfo.hasOpponent || ReceivedInfo.noBossEffects || ReceivedInfo.foeCharacter == null) return;

                if(GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType == NodeType.Boss && ReceivedInfo.foeCharacter.GetType() == typeof(SamGambit))
                {
                    if(debugMode) MelonLogger.Msg("Vs Sam");
                    ____encounterSummaryDisplayController.ShowBoss(new SamGambitBoss());
                }
                if(GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType == NodeType.Boss && ReceivedInfo.foeCharacter.GetType() == typeof(BonesBoss))
                {
                    ____encounterSummaryDisplayController.ShowBoss(new BonesBoss());
                }
            }
        }
        [HarmonyPatch(typeof(EncounterSummaryDisplayController), "ShowBoss", new System.Type[] { typeof(BossModifier) })]
        public static class UpdateBossModifierText_Patch
        {
            public static void Prefix(ref TextMeshProUGUI ____bossModTMP)
            {
                if(!ReceivedInfo.hasOpponent || ReceivedInfo.noBossEffects || ReceivedInfo.foeCharacter == null) return;

                if(GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType == NodeType.Boss && ReceivedInfo.foeCharacter.GetType() == typeof(SamGambit))
                {
                    SamGambitBoss.RandomizeChessPiece();
                    SamGambitBoss samGambitBoss = new SamGambitBoss();
                    samGambitBoss.SetFloorAdjustedModification(GameStatics.GetPlayer().CurrentRunProgress.GetStage() - 1, false);
                    ____bossModTMP.text = samGambitBoss.GetDescription();
                }
                
                if(GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType == NodeType.Boss && ReceivedInfo.foeCharacter.GetType() == typeof(BonesBoss))
                {
                    BonesBoss bonesBoss = new BonesBoss();
                    bonesBoss.SetFloorAdjustedModification(GameStatics.GetPlayer().CurrentRunProgress.GetStage() - 1, false);
                    ____bossModTMP.text = bonesBoss.GetDescription();
                }
            }
        }
        [HarmonyPatch(typeof(GridUtilitySingleton), "GenerateGrid", new System.Type[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(List<HistoricWord>), typeof(List<BossModifier>), typeof(List<BoardGenVizInfo>), typeof(bool), typeof(List<Tile>)}, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal })]
        public static class BossPreGridStuff_Patch
        {
            public static void Prefix(ref List<BossModifier> bossModifiers)
            {
                if(!ReceivedInfo.hasOpponent || ReceivedInfo.noBossEffects) return;

                try
                {
                    if(GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType == NodeType.Boss && bossModifiers.Select(modifier => modifier.GetType()).Contains(typeof(NatBoss)))
                    {
                        if(debugMode) MelonLogger.Msg("Vs Nat");
                        
                        encounterController.PulseBossModifier(typeof(NatBoss));

                        NatBoss natBoss = new NatBoss();
                        natBoss.SetFloorAdjustedModification(GameStatics.GetPlayer().CurrentRunProgress.GetStage() - 1, false);
                        natBoss.GetRandomizedUnavailableItems();
                    }
                }
                catch(System.Exception e)
                {
                    MelonLogger.Msg(e);
                }
            }
        }
        [HarmonyPatch(typeof(Player), "GetAllItems", new System.Type[] { typeof(bool) })]
        public static class NatHoldsStartOfGridItems_Patch
        {
            public static void Postfix(ref List<Item> __result)
            {
                try
                {
                    if(ReceivedInfo.hasOpponent && encounterController != null && GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType == NodeType.Boss && encounterController.GetBossModifiers().Select(m => m.GetType()).Contains(typeof(NatBoss)))
                    {
                        if(ReceivedInfo.foeCharacter?.GetType() == typeof(NathaServo) && !ReceivedInfo.noBossEffects)
                            __result = (from item in __result where !NatBoss.unavailableItemsList.Select(i => i.GetType()).Contains(item.GetType()) select item).ToList();
                    }
                }
                catch(System.Exception e)
                {
                    MelonLogger.Msg(e);
                }
            }
        }
        [HarmonyPatch(typeof(GridUtilitySingleton), "MakeStartOfGridBossAdjustments", new System.Type[] { typeof(GridData), typeof(List<BossModifier>), typeof(ChallengeRun), typeof(List<BoardGenVizInfo>), typeof(int), typeof(bool), typeof(bool)})]
        public static class BossGridModifiers_Patch
        {
            private static List<BossModifier> heldModifiers = new List<BossModifier>();
            public static void Prefix(ref List<BossModifier> bossModifiers, ref GridData gridData, ref List<BoardGenVizInfo> vizSteps)
            {
                if(ReceivedInfo.hasOpponent && ReceivedInfo.noBossEffects) bossModifiers = new List<BossModifier>();
                if(!ReceivedInfo.hasOpponent || ReceivedInfo.noBossEffects) return;

                //Rodman
                if(bossModifiers.Select(t => t.GetType()).Contains(typeof(RodmanBoss)))
                {
                    if(debugMode) MelonLogger.Msg("Vs Rodman");

                    List<Tile> list = new List<Tile>();
                    //Tile Color
                    foreach (Tile tile in gridData.GetAvailableTiles())
                    {
                        tile.SetTileType(new TileType[] { TileType.Red, TileType.Blue, TileType.Normal }[Random.Range(0,3)]);
                        list.Add(tile);
                    }
                    if (list.Count > 0)
                    {
                        BoardGenVizInfo item = new BoardGenVizInfo(gridData, null, list, false, typeof(ExtraQs), false, false, false, vizSteps[vizSteps.Count - 1].PlayerConsumableTiles);
                        vizSteps.Add(item);
                    }

                    list = new List<Tile>();
                    //Tile Score
                    foreach (Tile tile in gridData.GetAvailableTiles())
                    {
                        List<Tile> tilesAdjacentToCoordinates = GridUtility.Singleton.GetTilesAdjacentToCoordinates(gridData, tile.Coordinates, false);
                        List<TileType> list2 = new List<TileType>();
                        foreach (Tile tile2 in tilesAdjacentToCoordinates)
                        {
                            TileType tileType = tile2.GetTileType();
                            if (tileType != TileType.Normal && !list2.Contains(tileType))
                            {
                                list2.Add(tileType);
                            }
                        }
                        if (list2.Count > 0)
                        {
                            RodmanBoss rodmanBoss = new RodmanBoss();
                            rodmanBoss.SetFloorAdjustedModification(GameStatics.GetPlayer().CurrentRunProgress.GetStage(), false);

                            tile.ValueModifier -= (long)(rodmanBoss.FloorAdjustedModification * list2.Count);
                            list.Add(tile);
                        }
                    }
                    if (list.Count > 0)
                    {
                        vizSteps.Add(new BoardGenVizInfo(gridData, null, list, false, null, false, false, false, vizSteps[vizSteps.Count - 1].PlayerConsumableTiles));
                    }
                }

                //Nina In Scoring

                //Hayley Bayles (Literally Just AddNumbers)
                if(bossModifiers.Select(t => t.GetType()).Contains(typeof(HayleyBaylesBoss)))
                {
                    if(debugMode) MelonLogger.Msg("Vs Hayley");
                }

                //Sam Is In VerifyTileSelection

                //Bones In Scoring

                //Octacles
                if(bossModifiers.Select(t => t.GetType()).Contains(typeof(OctaclesBoss)))
                {
                    if(debugMode) MelonLogger.Msg("Vs Octacles");
                    //look for cursed tiles on the board
                    List<Tile> list = new List<Tile>();
                    List<Vector2Int> tileCoords = new List<Vector2Int>();
                    List<GlyphType> glyphs = new List<GlyphType>();
                    foreach (Tile tile in gridData.GetAvailableTiles())
                    {
                        if(tile.MyGlyphType != GlyphType.Letter)
                        {
                            tileCoords.Add(tile.Coordinates);
                            glyphs.Add(tile.MyGlyphType);
                        }
                    }

                    if(glyphs.Count > 0)
                    {
                        GlyphType thisGlyphType = glyphs[Random.Range(0, glyphs.Count)];
                        
                        foreach(Vector2Int tileCoordinates in tileCoords)
                        {
                            Tile tile = gridData.GetTileAtCoordinates(tileCoordinates);
                            if(tile.MyGlyphType != GlyphType.Letter)
                            {
                                switch(thisGlyphType)
                                {
                                    case GlyphType.Blank:
                                        tile.MyGlyphType = GlyphType.Blank;
                                        break;
                                    case GlyphType.Number:
                                    case GlyphType.Fraction:
                                        if(Random.Range(0, 2) == 0)
                                            tile.SetToRandomFraction();
                                        else
                                            tile.SetToRandomNumber();
                                        break;
                                    case GlyphType.Chess:
                                        tile.SetToRandomChessPiece();
                                        break;
                                    case GlyphType.BespokeCard:
                                        if(Random.Range(0, 5) == 0)
                                        {
                                            tile.SetGlyphType(GlyphType.BespokeCard);
                                            tile.SetSuit(Suit.Joker);
                                        }
                                        else
                                        {
                                            tile.SetToRandomLetter();
                                            tile.SetSuit(new Suit[] { Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Spades}[Random.Range(0, 4)]);
                                        }
                                        break;
                                    case GlyphType.Currency:
                                        tile.SetToRandomCurrency();
                                        break;
                                    case GlyphType.ScatteredItem:
                                        tile.SetToRandomItem();
                                        break;
                                    case GlyphType.Arrow:
                                        MelonLogger.Msg("Arrow is actually used!?!" + tile.GetStringRepresentation());
                                        break;
                                    default:
                                        if(debugMode) MelonLogger.Msg("Error: thisGlyphType isn't a valid glyph type");
                                        break;
                                }
                                list.Add(tile);
                            }
                        }
                        //update the grid
                        if (list.Count > 0)
                        {
                            BoardGenVizInfo item = new BoardGenVizInfo(gridData, null, list, false, typeof(OctaclesBoss), false, false, false, vizSteps[vizSteps.Count - 1].PlayerConsumableTiles);
                            vizSteps.Add(item);
                        }
                    }

                }

                //Nat In Pre-Grid

                //Sandy Is The Same
                //Meg Is Defensive
                //Human Boy Is The Same

                //remove boss modifiers for custom boss effect guys
                if(bossModifiers.Select(t => t.GetType()).Contains(typeof(PrismaticBeanBoss)))
                {
                    heldModifiers.Add(bossModifiers.FirstOrDefault(modifier => modifier.GetType() == typeof(PrismaticBeanBoss)));
                    bossModifiers.RemoveAll(modifier => modifier.GetType() == typeof(PrismaticBeanBoss));
                }
            }
            public static void Postfix(ref List<BossModifier> bossModifiers, ref GridData gridData, ref List<BoardGenVizInfo> vizSteps)
            {
                foreach(var modifier in heldModifiers)
                {
                    bossModifiers.Add(modifier);
                }
                heldModifiers.Clear();
                
                if(bossModifiers.Select(t => t.GetType()).Contains(typeof(PrismaticBeanBoss)))
                {
                    if(debugMode) MelonLogger.Msg("Vs Beans");

                    List<Tile> list = new List<Tile>();
                    List<Vector2Int> tileCoords = new List<Vector2Int>();

                    //Color Cursed Tiles
                    foreach (Tile tile in gridData.GetAvailableTiles())
                    {
                        if(tile.IsCursed())
                        {
                            tile.SetTileType(new TileType[] { TileType.Blue, TileType.Cactus, TileType.Gold, TileType.Green, TileType.Pink, TileType.Purple, TileType.Red, TileType.Void, TileType.White, TileType.Blue, TileType.Red, TileType.Void }[Random.Range(0,12)]);
                            list.Add(tile);
                        }
                    }

                    //update the grid first time
                    if (list.Count > 0)
                    {
                        BoardGenVizInfo item = new BoardGenVizInfo(gridData, null, list, false, typeof(OctaclesBoss), false, false, false, vizSteps[vizSteps.Count - 1].PlayerConsumableTiles);
                        vizSteps.Add(item);
                    }

                    List<Tile> list2 = new List<Tile>();
                    foreach (Tile tile in gridData.GetAvailableTiles())
                    {
                        if(tile.MyGlyphType != GlyphType.Letter && new TileType[] {TileType.Blue, TileType.Red, TileType.Void}.Contains(tile.MyTileType))
                        {
                            tile.MyGlyphType = GlyphType.Letter;
                            tile.SetLetter(Vocabulary.ActiveLanguageVocabulary.LanguageAlphabet.GetRandomConsonantWeighted());
					        tile.SetGlyphType(GlyphType.Letter);

                            list2.Add(tile);
                        }
                    }

                    //update the grid second time
                    if (list2.Count > 0)
                    {
                        BoardGenVizInfo item = new BoardGenVizInfo(gridData, null, list2, false, typeof(OctaclesBoss), false, false, false, vizSteps[vizSteps.Count - 1].PlayerConsumableTiles);
                        vizSteps.Add(item);
                    }
                }
            }
        }
        
        //Score
        [HarmonyPatch(typeof(EncounterController), "GetItemsForWordSubmission", new System.Type[] { typeof(List<TileSelection>), typeof(bool) })]
        public static class NatCaughtRedHanded_Patch
        {
            public static void Postfix(ref bool isIncludingInventory, ref List<Item> __result)
            {
                try
                {
                    if(isIncludingInventory && encounterController != null && GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType == NodeType.Boss && encounterController.GetBossModifiers().Select(m => m.GetType()).Contains(typeof(NatBoss)))
                    {
                        int resultCount = __result.Count;
                        __result.RemoveAll(item => GameStatics.GetPlayer().GetAllItems(false).Contains(item) && NatBoss.unavailableItemsList.Select(i => i.GetType()).Contains(item.GetType()));
                    }
                }
                catch(System.Exception e)
                {
                    MelonLogger.Msg(e);
                }
            }
        }
        [HarmonyPatch(typeof(ScoreCalculation), "ApplyBossModifier", new System.Type[] { typeof(List<TileSelection>), typeof(List<ScoreCalcVizInfo>), typeof(BossModifier) })]
        public static class BossScoreModifiers_Patch
        {
            public static void Postfix(ref ScoreCalcVizInfo __result, ref BossModifier bossModifier, ref List<TileSelection> tiles)
            {
                if(!ReceivedInfo.hasOpponent || ReceivedInfo.noBossEffects) return;

                if(bossModifier is NinaNixBoss)
                {
                    if(debugMode) MelonLogger.Msg("Vs Nina");
                    float num = 1f;
                    List<Tile> theseTiles = tiles.Select(tileSelection => tileSelection.SelectedTile).ToList();
                    foreach (Tile tile in theseTiles)
                    {
                        num *= -0.95f;
                    }
                    __result.WordBonus = new WordBonusToken((long)Mathf.RoundToInt(num * 100f), true);
                    __result.LettersInWordToPulse.AddRange(theseTiles);
                }
                if(bossModifier is BonesBoss)
                {
                    if(debugMode) MelonLogger.Msg("Vs Bones");
                    __result.WordBonus = new WordBonusToken((long)-1 * BonesBoss.wordScoreTaken / 2, false);
                }
            }
        }
        
        //Sam Gambit's Gambit
        [HarmonyPatch(typeof(GridUtilitySingleton), "GetValidNextTiles", new System.Type[] { typeof(GridData), typeof(List<Tile>), typeof(TileSelectionManager), typeof(bool) })]
        public static class TheGambit_Patch
        {
            public static void Postfix(ref GridUtilitySingleton __instance, ref List<TileSelection> __result, ref GridData gridData, ref TileSelectionManager tileSelectionManager, ref List<Tile> currentTiles)
            {
                if(!ReceivedInfo.hasOpponent || (encounterController != null && !encounterController.GetBossModifiers().Select(t => t.GetType()).Contains(typeof(SamGambitBoss)))) return;

                try
                {
                    //Normal Piece Change (Chess Pieces Don't Change) (King doesn't change anything, so no worry with that)
                    if(currentTiles.Count > 0 && currentTiles[currentTiles.Count - 1].MyGlyphType != GlyphType.Chess && SamGambitBoss.chessPiece != ChessPiece.King)
                    {
                        List<TileSelection> validTiles = __result;

                        GridUtilitySingleton instance = __instance;
                        GridData grid = gridData;
                        List<Tile> currTiles = currentTiles;
                        validTiles.RemoveAll(tile => (from adjTile in instance.GetTilesAdjacentToCoordinates(grid, currTiles[currTiles.Count - 1].Coordinates, GameStatics.GetPlayer().GetAllItems().Exists(item => item is HungrySnake)) select adjTile).Count(tileSelected => tileSelected.Coordinates == tile.SelectedTile.Coordinates) > 0);

                        Tile tileTemp = currentTiles[currentTiles.Count - 1];
                        tileTemp.PieceType = SamGambitBoss.chessPiece;
                        
                        List<Item> inventory = GameStatics.GetPlayer().GetAllItems();
                        if(!inventory.Exists(item => item is KingOfTheBridge)) inventory.Add(new KingOfTheBridge());

                        validTiles.AddRange(ChessPieces.GetValidChessMoves(gridData, inventory, tileTemp, tileSelectionManager));
                        tileTemp.PieceType = ChessPiece.None;
                        __result = validTiles;
                    }
                }
                catch (System.Exception e)
                {
                    MelonLogger.Msg(e);
                }
            }
        }
        
        //Nat's Thievery
        [HarmonyPatch(typeof(InventoryVisualController), "PopulateStickers")]
        public static class NatTakesStickers_Patch
        {
            public static void Postfix(ref List<ItemObject> ____stickerObjects)
            {
                if(!ReceivedInfo.hasOpponent) return;
                
                try
                {
                    Player player = GameStatics.GetPlayer();
                    if(player.CurrentRunProgress.GetCurrentNodeType() == NodeType.Boss)
                    {
                        if(encounterController != null && encounterController.GetBossModifiers().Select(m => m.GetType()).Contains(typeof(NatBoss)))
                        {
                            foreach(ItemObject item in ____stickerObjects)
                            {
                                if(item != null && NatBoss.unavailableItemsList.Select(i => i.GetType()).Contains(item.MyItem.GetType()))
                                {
                                    SDFImage itemImage = item.GetComponentInChildren<nickeltin.SDF.Runtime.SDFImage>();
                                    if(itemImage != null) itemImage.color = UnityEngine.Color.black;
                                }
                            }
                        }
                    }
                }
                catch(System.Exception e)
                {
                    MelonLogger.Msg(e);
                }
            }
        }
        [HarmonyPatch(typeof(InventoryVisualController), "PopulateStamps")]
        public static class NatTakesStamps_Patch
        {
            public static void Postfix(ref List<ItemObject> ____stampObjects)
            {
                if(!ReceivedInfo.hasOpponent) return;

                try
                {
                    Player player = GameStatics.GetPlayer();
                    if(player.CurrentRunProgress.GetCurrentNodeType() == NodeType.Boss)
                    {
                        if(encounterController != null && encounterController.GetBossModifiers().Select(m => m.GetType()).Contains(typeof(NatBoss)))
                        {
                            foreach(ItemObject item in ____stampObjects)
                            {
                                if(item != null && NatBoss.unavailableItemsList.Select(i => i.GetType()).Contains(item.MyItem.GetType()))
                                {
                                    SDFImage stampOutline = item.GetComponentInChildren<nickeltin.SDF.Runtime.SDFImage>();
                                    stampOutline.color = UnityEngine.Color.black;
                                    
                                    Image[] itemImages = item.GetComponentsInChildren<Image>();
                                    foreach(Image itemImage in itemImages)
                                    {
                                        itemImage.color = UnityEngine.Color.black;
                                    }

                                    if(debugMode) MelonLogger.Msg("Found Item: " + item.MyItem.Name);
                                }
                            }
                        }
                    }
                }
                catch(System.Exception e)
                {
                    MelonLogger.Msg(e);
                }
            }
        }

        //Remove Meg's Normal Ability
        [HarmonyPatch(typeof(EncounterController), "IsBossModifierActive")]
        public static class NoMoneyForYou_Patch
        {
            public static void Postfix(ref System.Type bossModifierType, ref bool __result)
            {
                if((bossModifierType == typeof(CretaceousMegBoss) && ReceivedInfo.hasOpponent) || ReceivedInfo.noBossEffects || bossModifierType == typeof(RandomiseItemOrder))
                {
                    __result = false;
                }
            }
        }
        public static void SetMegMoney(int money)
        {
            if(money < 0) money = 100000;
            GameStatics.GetPlayer().CurrentRunProgress.CurrentRunStatistics.TotalCashEarned += money;
            GameStatics.GetPlayer().ChangeMoney(money);
        }
        
        //Meg's Money
        [HarmonyPatch(typeof(EncounterController), "ShowScoreCalculation", new System.Type[] { typeof(List<ScoreCalcVizInfo>), typeof(HistoricWord), typeof(ScorePacket), typeof(ScorePacket), typeof(List<Tile>), typeof(System.Collections.IEnumerator) })]
        public static class MegDoesTaxes_Patch
        {
            public static void Prefix(ref List<ScoreCalcVizInfo> steps, ref int ____remainingGrids)
            {
                if(____remainingGrids > 0) return;

                int income = (int)(ReceivedInfo.opponentHighscore.Score / Mathf.Pow(4, GameStatics.GetPlayer().CurrentRunProgress.GetStage()));
                steps[0].EarningsBreakdown["Meg's Income"] = income;
                SetMegMoney(income);

                if(debugMode) MelonLogger.Msg(ReceivedInfo.opponentHighscore + " | " + income);
            }
        }

        //Stop Human Boy If You Should
        [HarmonyPatch(typeof(HumanBoyBoss), "GetItemToSteal")]
        public static class SwiperNoSwiping_Patch
        {
            public static void Postfix(ref Item __result)
            {
                if(ReceivedInfo.hasOpponent) __result = null;
            }
        }

        //Descs For Bosses That Already Exist
            #region Desc Overrides
            [HarmonyPatch(typeof(CretaceousMegBoss), "GetDescription")]
            public static class MegBossDesc_Patch
            {
                public static void Postfix(ref string __result)
                {
                    if(!ReceivedInfo.hasOpponent || ReceivedInfo.noBossEffects) return;

                    __result = "Meg gets money based on your highest scoring word this encounter.";
                }
            }
            [HarmonyPatch(typeof(HumanBoyBoss), "GetDescription")]
            public static class HumanBoyDesc_Patch
            {
                public static void Postfix(ref string __result)
                {
                    if(!ReceivedInfo.hasOpponent || ReceivedInfo.noBossEffects) return;

                    __result = "Decreases all Stickers by 1 Level for the fight (Minimum level: 1)";
                }
            }
            [HarmonyPatch(typeof(PrismaticBeanBoss), "GetDescription")]
            public static class BeansBossDesc_Patch
            {
                public static void Postfix(ref string __result)
                {
                    if(!ReceivedInfo.hasOpponent || ReceivedInfo.noBossEffects) return;

                    __result = string.Format("Colors all cursed tiles randomly. Any coloured <#3C83C8>RED</color>, <#D2504D>BLUE</color>, or <#6F2E87>VOID</color> are replaced with Consonants", new object[]
                    {
                        Tile.ChangeTileTypeToString(TileType.Blue),
                        Tile.ChangeTileTypeToString(TileType.Void)
                    });
                }
            }
            #endregion
        
        //Descs For Boss Effects On Select Screen
        [HarmonyPatch(typeof(Character), "GetDescription")]
        public static class CharEffect_Patch
        {
            public static void Postfix(ref string __result, ref Character __instance)
            {
                if(__instance.GetType() == typeof(PrismaticBean) || CursedUI.lobbyID == CSteamID.Nil) return;

                BossModifier bossModifier = new RandomiseItemOrder();

                if(__instance.GetType() == typeof(WetDennis))
                    bossModifier = new RodmanBoss();
                else if(__instance.GetType() == typeof(NinaNix))
                    bossModifier = new NinaNixBoss();
                else if(__instance.GetType() == typeof(HayleyBayles))
                    bossModifier = new HayleyBaylesBoss();
                else if(__instance.GetType() == typeof(SamGambit))
                    bossModifier = new SamGambitBoss();
                else if(__instance.GetType() == typeof(BonesTheDog))
                    bossModifier = new BonesBoss();
                else if(__instance.GetType() == typeof(Octacles))
                    bossModifier = new OctaclesBoss();
                else if(__instance.GetType() == typeof(NathaServo))
                    bossModifier = new NatBoss();
                else if(__instance.GetType() == typeof(SandySaguaro))
                    bossModifier = new SandySaguaroBoss();
                else if(__instance.GetType() == typeof(Spike))
                    bossModifier = new CretaceousMegBoss();
                else if(__instance.GetType() == typeof(SockHead))
                    bossModifier = new HumanBoyBoss();
                else if(__instance.GetType() == typeof(PrismaticBean))
                    bossModifier = new PrismaticBeanBoss();
                else if(debugMode) MelonLogger.Msg("Option Is An Invalid Character");
                
                if(bossModifier.GetType() == typeof(CretaceousMegBoss))
                {
                    __result = "As a boss:\n\n" + "Meg gets money based on the foe's highest scoring word for the encounter.";
                    return;
                }
                if(bossModifier.GetType() == typeof(HumanBoyBoss))
                {
                    __result = "As a boss:\n\n" + "Decreases all Stickers by 1 Level for the fight (Minimum level: 1).";
                    return;
                }

                bossModifier.SetFloorAdjustedModification(0, false);
                __result = "As a boss:\n\n" + bossModifier.GetDescription();
            }
        }
        [HarmonyPatch(typeof(PrismaticBean), "GetDescription")]
        public static class BeansJustHadToBeDifferent_Patch
        {
            public static void Postfix(ref string __result)
            {
                if(CursedUI.lobbyID == CSteamID.Nil) return;
                __result = "As a boss:\n\n" + "Colours all cursed tiles randomly. Any coloured <color=red>RED</color>, <color=blue>BLUE</color>, or <color=purple>VOID</color> are replaced with Consonants.";
            }
        }
        #endregion
    }
    public static class ReceivedInfo
    {
        public static bool noBossEffects = false, delayScoreUpdates = false;
        public static bool hasOpponent = false;
        public static bool opponentIsInBoss = false;
        public static ScorePacket opponentHighscore = new ScorePacket(0);
        public static int opponentHealth = 3;
        public static Character foeCharacter = null;

        public static void ResetInfo()
        {
            hasOpponent = false;
            opponentIsInBoss = false;
            opponentHighscore = new ScorePacket(0);
            opponentHealth = 3;
            foeCharacter = null;
        }
        public static void SetFoeCharacter(string characterName)
        {
            List<System.Type> characterTypes = Character.GetAllCharacters();
            foreach (var charType in characterTypes)
            {
                Character thisCharacter = (Character)System.Activator.CreateInstance(charType);
                if(thisCharacter.GetName() == characterName)
                {
                    foeCharacter = thisCharacter;
                    return;
                }
            }
            if(MultiplayerManager.debugMode) MelonLogger.Msg("Error: Couldn't Find Character");
        }
    }
    public class CursedNetworking
    {
        #region Public Variables
        public static bool isHost = false;
        public static bool playerDataChanged = true;
        public struct PlayerPacket
        {
            public string playerName;
            public bool inBoss;
            public ScorePacket highScore;
            public int health;
            public string myCharacterName;
            public PlayerPacket(string name, int totHealth)
            {
                playerName = name;
                UpdatePacket(false, new ScorePacket(0), totHealth);
                myCharacterName = "";
            }
            public void UpdatePacket(bool inBossFight, ScorePacket hScore, int currHealth)
            {
                inBoss = inBossFight;
                highScore = hScore;
                health = currHealth;
                playerDataChanged = true;
            }
            public string GetAsString(char divider)
            {
                string charName = myCharacterName != "" ? ":" + myCharacterName : "";
                string dataString = playerName + divider + inBoss + divider + highScore.Score + divider + health + charName;
                return dataString;
            }
            public void ResetPacket(bool fullReset = false)
            {
                if(fullReset) playerName = "";
                inBoss = false;
                highScore = new ScorePacket(0);
                health = 0;
            }
        }
        public static PlayerPacket myPlayerPacket;
        #endregion

        public static void SetUpNetworking() //Called Once At Start (Includes Health Hard-Set)
        {
            System.Environment.SetEnvironmentVariable("SteamAppId", "3856460");
            System.Environment.SetEnvironmentVariable("SteamGameId", "3856460");

            if(MultiplayerManager.debugMode) MelonLogger.Msg("Steam Linked!");

            myPlayerPacket = new PlayerPacket("", 3);

            new CursedUI().SetUpUI();
        }
        public static void UpdateAndSendPlayerPacket(bool isLobbyOptions = false)
        {
            if((CursedUI.lobbyID == CSteamID.Nil || !playerDataChanged) && !isLobbyOptions) return;
            
            if(!isLobbyOptions)
            {
                SteamMatchmaking.SetLobbyMemberData(CursedUI.lobbyID, "PlayerPacket", myPlayerPacket.GetAsString(':'));
                if(MultiplayerManager.debugMode) MelonLogger.Msg("Updated Info To: " + myPlayerPacket.GetAsString(':'));
                playerDataChanged = false;
                return;
            }

            string lobbyOptions = "";
            
            if(ReceivedInfo.noBossEffects) lobbyOptions += lobbyOptions == "" ? "noBossEffects" : ":noBossEffects";
            if(ReceivedInfo.delayScoreUpdates) lobbyOptions += lobbyOptions == "" ? "delayScoreUpdates" : ":delayScoreUpdates";
            
            SteamMatchmaking.SetLobbyData(CursedUI.lobbyID, "LobbyOptions", lobbyOptions);
            if(MultiplayerManager.debugMode) MelonLogger.Msg("Updated Lobby Options To: " + lobbyOptions);
        }
        public static void ReceiveAndUpdateFoeInfo(LobbyDataUpdate_t callback)
        {
            if(callback.m_bSuccess == 0)
            {
                if(MultiplayerManager.debugMode) MelonLogger.Msg("Failed To Retrieve Data For Lobby: " + callback.m_ulSteamIDLobby);
                return;
            }

            if(callback.m_ulSteamIDLobby == callback.m_ulSteamIDMember)
            {
                string[] lobbyOptions = SteamMatchmaking.GetLobbyData((CSteamID)callback.m_ulSteamIDLobby, "LobbyOptions").Split(':');
                
                ReceivedInfo.noBossEffects = lobbyOptions.Contains("noBossEffects");
                ReceivedInfo.delayScoreUpdates = lobbyOptions.Contains("delayScoreUpdates");

                if(MultiplayerManager.debugMode) MelonLogger.Msg(ReceivedInfo.noBossEffects + " | " + ReceivedInfo.delayScoreUpdates);
            }

            string[] lobbyDataList = new string[5];
            for(int i = 0; i < 2; i++)
            {
                var member = SteamMatchmaking.GetLobbyMemberByIndex((CSteamID)callback.m_ulSteamIDLobby, i);
                string[] tempLobbyDataList = SteamMatchmaking.GetLobbyMemberData((CSteamID)callback.m_ulSteamIDLobby, member, "PlayerPacket").Split(':');
                if(myPlayerPacket.playerName == "Player 1" && tempLobbyDataList.Contains("Player 2"))
                {
                    lobbyDataList = tempLobbyDataList;
                }
                else if(myPlayerPacket.playerName == "Player 2" && tempLobbyDataList.Contains("Player 1"))
                {
                    lobbyDataList = tempLobbyDataList;
                }
            }

            if(long.TryParse(lobbyDataList[2], out long highScoreLong) && int.TryParse(lobbyDataList[3], out int health))
            {
                if(lobbyDataList[0] != myPlayerPacket.playerName)
                {
                    ReceivedInfo.opponentIsInBoss = lobbyDataList[1] == "True";

                    ReceivedInfo.opponentHighscore = new ScorePacket(highScoreLong);
                    ReceivedInfo.opponentHealth = health;
                    if(MultiplayerManager.debugMode) MelonLogger.Msg("Received Info: " + string.Join(" | ", lobbyDataList));
                    if(!ReceivedInfo.hasOpponent)
                    {
                        ReceivedInfo.hasOpponent = true;
                        if(MultiplayerManager.debugMode) MelonLogger.Msg("You Now Have An Opponent!");
                    }
                    if(!string.IsNullOrEmpty(lobbyDataList[4]))
                    {
                        ReceivedInfo.SetFoeCharacter(lobbyDataList[4]);
                    }
                    else
                    {
                        ReceivedInfo.foeCharacter = new PrismaticBean();
                    }
                }
                else
                {
                    if(MultiplayerManager.debugMode) MelonLogger.Msg("You Updated Info To: " + string.Join(" | ", lobbyDataList));
                }
            }
            else if(lobbyDataList.Count() == 4)
            {
                if(MultiplayerManager.debugMode) MelonLogger.Msg("Failed To Update Player Packet Info - Ints Didn't Parse: " + string.Join(" | ", lobbyDataList));
            }
        }
    }
    public class CursedUI
    {
        #region GameObjects
        public static GameObject canvasObj = new GameObject("Canvas", new System.Type[] { typeof(Canvas), typeof(RectTransform), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CursedUI), typeof(UnityEngine.UI.Image) });
        public  static GameObject eventSystemObj = new GameObject("EventSystem", new System.Type[] { typeof(Transform), typeof(EventSystem), typeof(InputSystemUIInputModule), typeof(CursedUI) });
        public  static GameObject lobbyMenuObj = new GameObject("Lobbies Menu", new System.Type[] { typeof(RectTransform), typeof(CursedUI) });
        public  static GameObject scrollViewObj = new GameObject("Scorll View", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(CursedUI) });
        public static GameObject showLobbyButtonObj = new GameObject("Show Lobby Button", new System.Type[] {typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(TextMeshProUGUI), typeof(CursedUI) });
        public static GameObject hideLobbyButtonObj = new GameObject("Hide Lobby Button", new System.Type[] {typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(TextMeshProUGUI), typeof(CursedUI) });
        public  static GameObject hostButtonObj = new GameObject("Host Button", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(TextMeshProUGUI), typeof(CursedUI) });
        public  static GameObject lobbyIDObj = new GameObject("Lobby ID", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public  static GameObject lobbyIDBackgroundObj = new GameObject("Lobby ID Background", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(CursedUI) });
        public  static GameObject lobbyButtonObj = new GameObject("Lobby Button", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(TextMeshProUGUI), typeof(CursedUI) });
        public  static GameObject lobbyNameInputFieldObj = new GameObject("Lobby Name Input Field", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(TMP_InputField), typeof(CursedUI) });
        public  static GameObject inputFieldTextObj = new GameObject("Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public  static GameObject inputFieldPlaceholderObj = new GameObject("Placeholder", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public  static GameObject joinLobbyButtonObj = new GameObject("Join Lobby Button", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(TextMeshProUGUI), typeof(CursedUI) });
        public  static GameObject backButtonObj = new GameObject("Back Button", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(TextMeshProUGUI), typeof(CursedUI) });
        public  static List<GameObject> lobbyObjects = new List<GameObject> { canvasObj, eventSystemObj, showLobbyButtonObj, hideLobbyButtonObj, hostButtonObj, lobbyIDObj, lobbyButtonObj, lobbyMenuObj, backButtonObj, lobbyNameInputFieldObj, joinLobbyButtonObj, lobbyIDBackgroundObj };

        public static GameObject waitingTextObj = new GameObject("Waiting Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public static GameObject menuButtonCoverObj = new GameObject("Menu Cover", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(Image) });
        public static GameObject bossEffectsToggleObj = new GameObject("Boss Effects Toggle", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(CursedUI) });
        public  static GameObject noBossEffectsTextObj = new GameObject("Boss Effects Toggle Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(UnityEngine.UI.Outline) });

        public  static GameObject showLobbyButtonTextObj = new GameObject("Show Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public  static GameObject hideLobbyButtonTextObj = new GameObject("Hide Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public  static GameObject hostLobbyButtonTextObj = new GameObject("Host Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public  static GameObject lobbyButtonTextObj = new GameObject("Lobby Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public  static GameObject joinLobbyButtonTextObj = new GameObject("Join Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public  static GameObject backLobbyButtonTextObj = new GameObject("Back Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        #endregion
        #region Steam Callbacks
        public static Callback<LobbyMatchList_t> m_lobbyMatchList;
        public static Callback<LobbyEnter_t> m_lobbyEnter;
        public static Callback<LobbyCreated_t> m_lobbyCreated;
        public static Callback<LobbyDataUpdate_t> m_updateData;
        #endregion
        public static bool isUIOpen = false;
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
            bossEffectsToggleObj.transform.SetParent(lobbyIDObj.transform);
            waitingTextObj.transform.SetParent(canvasObj.transform);
            menuButtonCoverObj.transform.SetParent(canvasObj.transform);
                //Text Objects
                showLobbyButtonTextObj.transform.SetParent(showLobbyButtonObj.transform);
                hideLobbyButtonTextObj.transform.SetParent(hideLobbyButtonObj.transform);
                hostLobbyButtonTextObj.transform.SetParent(hostButtonObj.transform);
                lobbyButtonTextObj.transform.SetParent(lobbyButtonObj.transform);
                joinLobbyButtonTextObj.transform.SetParent(joinLobbyButtonObj.transform);
                backLobbyButtonTextObj.transform.SetParent(backButtonObj.transform);
                noBossEffectsTextObj.transform.SetParent(bossEffectsToggleObj.transform);
                //In-Game UI
                myHeartsObj.transform.SetParent(canvasObj.transform);
                foeHeartsObj.transform.SetParent(canvasObj.transform);
                overrideWaitingButtonObj.transform.SetParent(canvasObj.transform);
                overrideWaitingButtonTextObj.transform.SetParent(overrideWaitingButtonObj.transform);
            
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
                    lobbyIDText.fontSize = 80;
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
                    waitingText.text = "WAITING FOR OPPONENT...";
                    waitingText.autoSizeTextContainer = true;
                    waitingText.alignment = TextAlignmentOptions.Center;
                }


            TextMeshProUGUI showLobbyButtonText = showLobbyButtonTextObj.GetComponent<TextMeshProUGUI>();
            if(showLobbyButtonText != null)
            {
                showLobbyButtonText.text = "OPEN";
                showLobbyButtonText.alignment = TextAlignmentOptions.Center;
            }
            TextMeshProUGUI hideLobbyButtonText = hideLobbyButtonTextObj.GetComponent<TextMeshProUGUI>();
            if(hideLobbyButtonText != null)
            {
                hideLobbyButtonText.text = "CLOSE";
                hideLobbyButtonText.alignment = TextAlignmentOptions.Center;
            }
            TextMeshProUGUI hostLobbyButtonText = hostLobbyButtonTextObj.GetComponent<TextMeshProUGUI>();
            if(hostLobbyButtonText != null)
            {
                hostLobbyButtonText.text = "HOST";
                hostLobbyButtonText.fontSize = 80;
                hostLobbyButtonText.alignment = TextAlignmentOptions.Center;
            }
            TextMeshProUGUI lobbyButtonText = lobbyButtonTextObj.GetComponent<TextMeshProUGUI>();
            if(lobbyButtonText != null)
            {
                lobbyButtonText.text = "JOIN";
                lobbyButtonText.fontSize = 85;
                lobbyButtonText.alignment = TextAlignmentOptions.Center;
            }
            TextMeshProUGUI joinLobbyButtonText = joinLobbyButtonTextObj.GetComponent<TextMeshProUGUI>();
            if(joinLobbyButtonText != null)
            {
                joinLobbyButtonText.text = "ENTER";
                joinLobbyButtonText.fontSize = 75;
                joinLobbyButtonText.alignment = TextAlignmentOptions.Center;
            }
            TextMeshProUGUI backLobbyButtonText = backLobbyButtonTextObj.GetComponent<TextMeshProUGUI>();
            if(backLobbyButtonText != null)
            {
                backLobbyButtonText.text = "LEAVE";
                backLobbyButtonText.fontSize = 40;
                backLobbyButtonText.alignment = TextAlignmentOptions.Center;
            }
            
            TextMeshProUGUI overrideWaitingButtonText = overrideWaitingButtonTextObj.GetComponent<TextMeshProUGUI>();
            if(overrideWaitingButtonText != null)
            {
                overrideWaitingButtonText.text = "OVERRIDE";
                overrideWaitingButtonText.color = Color.black;
                overrideWaitingButtonText.fontSize = 14;
                overrideWaitingButtonText.alignment = TextAlignmentOptions.Center;
            }

            RectTransform noBossEffectsTextRect = noBossEffectsTextObj.GetComponent<RectTransform>();
            if(noBossEffectsTextRect != null)
            {
                noBossEffectsTextRect.localPosition = new Vector3(-175, 0, 0);
                noBossEffectsTextRect.sizeDelta = new Vector3(500, 50);
            }
                TextMeshProUGUI noBossEffectsText = noBossEffectsTextObj.GetComponent<TextMeshProUGUI>();
                if(noBossEffectsText != null)
                {
                    noBossEffectsText.text = "BOSS EFFECTS: ";
                    noBossEffectsText.color = Color.black;
                    noBossEffectsText.autoSizeTextContainer = true;
                    noBossEffectsText.alignment = TextAlignmentOptions.Center;
                }
            #endregion
            #region Buttons Appearance
            RectTransform showLobbyButtonRect = showLobbyButtonObj.GetComponent<RectTransform>();
            if(showLobbyButtonRect != null)
            {
                showLobbyButtonRect.localPosition = new Vector3(-1 * Screen.width * 3 / 8, Screen.height * 13 / 32, 0);
                showLobbyButtonRect.sizeDelta = new Vector2(200, 100);
            }
            RectTransform hideLobbyButtonRect = hideLobbyButtonObj.GetComponent<RectTransform>();
            if(hideLobbyButtonRect != null)
            {
                hideLobbyButtonRect.localPosition = new Vector3(-1 * Screen.width * 3 / 8, Screen.height * 13 / 32, 0);
                hideLobbyButtonRect.sizeDelta = new Vector2(200, 100);
            }
            RectTransform hostButtonRect = hostButtonObj.GetComponent<RectTransform>();
            if(hostButtonRect != null)
            {
                hostButtonRect.localPosition = new Vector3(0, Screen.height / 10, 0);
                hostButtonRect.sizeDelta = new Vector2(300, 150);
            }
                UnityEngine.UI.Image hostButtonImg = hostButtonObj.GetComponent<Image>();
                if(hostButtonImg != null)
                {
                    hostButtonImg.color = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);
                }
                RectTransform hostTextRect = hostLobbyButtonTextObj.GetComponent<RectTransform>();
                if(hostTextRect != null)
                {
                    hostTextRect.sizeDelta = new Vector2(300, 150);
                }
            RectTransform lobbyButtonRect = lobbyButtonObj.GetComponent<RectTransform>();
            if(lobbyButtonRect != null)
            {
                lobbyButtonRect.localPosition = new Vector3(0, -1 * Screen.height / 10, 0);
                lobbyButtonRect.sizeDelta = new Vector2(300, 150);
            }
                UnityEngine.UI.Image lobbyButtonImg = lobbyButtonObj.GetComponent<Image>();
                if(lobbyButtonImg != null)
                {
                    lobbyButtonImg.color = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);
                }
                RectTransform lobbyTextRect = lobbyButtonTextObj.GetComponent<RectTransform>();
                if(lobbyTextRect != null)
                {
                    lobbyTextRect.sizeDelta = new Vector2(300, 150);
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
                joinButtonRect.sizeDelta = new Vector2(250, 100);
            }
                UnityEngine.UI.Image joinButtonImage = joinLobbyButtonObj.GetComponent<UnityEngine.UI.Image>();
                if(joinButtonImage != null)
                {
                    joinButtonImage.color = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);
                }
                TextMeshProUGUI joinButtonText = joinLobbyButtonObj.GetComponent<TextMeshProUGUI>();
                if(joinButtonText != null)
                {
                    joinButtonText.text = "JOIN LOBBY";
                    joinButtonText.color = new UnityEngine.Color(1, 1, 1, 1);
                    joinButtonText.alignment = TextAlignmentOptions.Center;
                }
            RectTransform overrideWaitingButtonRect = overrideWaitingButtonObj.GetComponent<RectTransform>();
            if(overrideWaitingButtonRect != null)
            {
                overrideWaitingButtonRect.localPosition = new Vector3(Screen.width * 7 / 16, Screen.height * 7 / 16, 0);
                overrideWaitingButtonRect.sizeDelta = new Vector2(100, 25);
            }
                RectTransform overrideWaitingButtonTextRect = overrideWaitingButtonTextObj.GetComponent<RectTransform>();
                if(overrideWaitingButtonTextRect != null)
                {
                    overrideWaitingButtonTextRect.localPosition = Vector3.zero;
                    overrideWaitingButtonTextRect.sizeDelta = overrideWaitingButtonRect.sizeDelta;
                }
            RectTransform bossEffectToggleRect = bossEffectsToggleObj.GetComponent<RectTransform>();
            if(bossEffectToggleRect != null)
            {
                bossEffectToggleRect.localPosition = new Vector3(Screen.width * 7 / 16, Screen.height / 10 * 1.75f, 0);
                bossEffectToggleRect.sizeDelta = new Vector2(50, 50);
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
            RectTransform menuButtonCoverRect = menuButtonCoverObj.GetComponent<RectTransform>();
            if(menuButtonCoverRect != null)
            {
                menuButtonCoverRect.localPosition = new Vector3(-3 * Screen.width / 8, -7 * Screen.height / 16, 0);
                menuButtonCoverRect.sizeDelta = new Vector2(4 * Screen.width / 19, Screen.height / 10);
            }
                UnityEngine.UI.Image menuButtonCoverImg = menuButtonCoverObj.GetComponent<Image>();
                if(menuButtonCoverImg != null)
                {
                    menuButtonCoverImg.color = new UnityEngine.Color(0, 0, 0, 0);
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
                    placeholder.text = "ENTER LOBBY ID";
                    placeholder.color = new UnityEngine.Color(0.7f, 0.7f, 0.7f, 0.5f);
                    placeholder.alignment = TextAlignmentOptions.Center;
                    placeholder.fontSize = 80;
                }
                
                // Setup input field properties
                inputField.textComponent = textComponent;
                inputField.placeholder = placeholder;
                inputField.caretColor = new UnityEngine.Color(1, 1, 1, 1);
                inputField.caretWidth = 1;
                inputField.selectionColor = new UnityEngine.Color(0.65f, 0.8f, 1, 0.75f);
            }
            #endregion
            #region In-Game UI
            RectTransform myHeartsRect = myHeartsObj.GetComponent<RectTransform>();
            if(myHeartsRect != null)
            {
                myHeartsRect.localPosition = new Vector3(6.75f * Screen.width / 19, Screen.height / 2 - 55, 0);
                myHeartsRect.sizeDelta = new Vector2(100, 50);

                myHeartsObj.GetComponent<TextMeshProUGUI>().autoSizeTextContainer = true;
                foeHeartsObj.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
            }
            RectTransform foeHeartsRect = foeHeartsObj.GetComponent<RectTransform>();
            if(foeHeartsRect != null)
            {
                foeHeartsRect.localPosition = new Vector3(-6.6f * Screen.width / 19, Screen.height / 2 - 45, 0);
                foeHeartsRect.sizeDelta = new Vector2(100, 50);

                foeHeartsObj.GetComponent<TextMeshProUGUI>().autoSizeTextContainer = true;
                foeHeartsObj.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
            }
            #endregion
        }
        public void SetUpUI() //Called Once On Game Start
        {
            SetUIHeirarchy();
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
            Button overrideWaitingTextButton = overrideWaitingButtonObj.GetComponent<Button>();
            if(overrideWaitingTextButton != null)
            {
                overrideWaitingTextButton.onClick.AddListener(OverrideWaiting);
            }
            Button bossEffectsToggle = bossEffectsToggleObj.GetComponent<Button>();
            if(bossEffectsToggle != null)
            {
                bossEffectsToggle.onClick.AddListener(ToggleBossEffects);
            }
            #endregion
            
            #region Steam Callbacks
            m_lobbyMatchList = Callback<LobbyMatchList_t>.Create(OnLobbyMatchList);
            m_lobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            m_updateData = Callback<LobbyDataUpdate_t>.Create(CursedNetworking.ReceiveAndUpdateFoeInfo);
            m_lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            #endregion

            #region Init Setup
            //Iteration Through All
            foreach(var thisObject in lobbyObjects)
            {
                //Persistence
                Object.DontDestroyOnLoad(thisObject);

                //hidden
                thisObject.SetActive(new List<GameObject>{ canvasObj, eventSystemObj, showLobbyButtonObj }.Contains(thisObject));
            }
            waitingTextObj.SetActive(false);
            overrideWaitingButtonObj.SetActive(false);
            menuButtonCoverObj.SetActive(false);
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

            lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "WAITING...";
            
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
            waitingTextObj.SetActive(false);
        }
        public static void BackButtonPressed()
        {
            ReceivedInfo.ResetInfo();
            CursedNetworking.myPlayerPacket.ResetPacket();
            foreach(var thisObject in lobbyObjects)
            {
                thisObject.SetActive(!new List<GameObject> { backButtonObj, showLobbyButtonObj, lobbyNameInputFieldObj, lobbyIDBackgroundObj }.Contains(thisObject));
            }
            lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "WAITING...";
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
                    if(MultiplayerManager.debugMode) MelonLogger.Msg("Getting Random Lobby");
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

            CursedNetworking.myPlayerPacket.playerName = "Player 1";
            
            CursedNetworking.isHost = true;
        }
        public static void OverrideWaiting()
        {
            if(!ReceivedInfo.hasOpponent) return;

            ReceivedInfo.opponentHighscore = new ScorePacket(1);
            overrideWaitingButtonObj.SetActive(false);

            ReceivedInfo.opponentHealth = 3;
            CursedNetworking.myPlayerPacket.health = 3;

            if(CursedNetworking.myPlayerPacket.highScore > new ScorePacket(1))
                ReceivedInfo.opponentHealth = 4;
            else
                CursedNetworking.myPlayerPacket.health = 4;
        }
        public static void ToggleBossEffects()
        {
            if(!CursedNetworking.isHost) return;

            ReceivedInfo.noBossEffects = !ReceivedInfo.noBossEffects;
            CursedNetworking.UpdateAndSendPlayerPacket(true);
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
                    if(MultiplayerManager.debugMode) MelonLogger.Msg("Found Chosen Lobby! Joining...");
                    SteamMatchmaking.JoinLobby(lobby);
                    return;
                }
                else if(inputLobbyCode == "" && SteamMatchmaking.GetNumLobbyMembers(lobby) == 1)
                {
                    if(MultiplayerManager.debugMode) MelonLogger.Msg("Joining Random Lobby");
                    SteamMatchmaking.JoinLobby(lobby);
                    return;
                }
            }
            if(MultiplayerManager.debugMode) MelonLogger.Msg("Failed To Get A Lobby To Join");
            if(inputLobbyCode == "")
                lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "NO LOBBY FOUND";
            else
                lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "LOBBY NOT FOUND";
        }
        void OnLobbyEnter(LobbyEnter_t callback)
        {
            if(CursedNetworking.isHost) return;
            if(MultiplayerManager.debugMode) MelonLogger.Msg("Joined Lobby");

            if(SteamMatchmaking.GetNumLobbyMembers((CSteamID)callback.m_ulSteamIDLobby) <= 2)
            {
                CursedNetworking.myPlayerPacket.playerName = "Player 2";
                if(MultiplayerManager.debugMode) MelonLogger.Msg("You Are Player 2");
            }
            else
            {
                CursedNetworking.myPlayerPacket.playerName = "Spectator";
                if(MultiplayerManager.debugMode) MelonLogger.Msg("You Are Spectating");
            }

            if(callback.m_EChatRoomEnterResponse != 1)
            {
                if(MultiplayerManager.debugMode) MelonLogger.Msg("Failed To Enter Lobby: " + (uint)callback.m_EChatRoomEnterResponse);
                return;
            }
            lobbyID = (CSteamID)callback.m_ulSteamIDLobby;
            lobbyName = ((ulong)lobbyID % 10000).ToString("D4");
            lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "LOBBY ID: " + lobbyName;
        }
        void OnLobbyCreated(LobbyCreated_t callback)
        {
            if(callback.m_eResult != EResult.k_EResultOK)
            {
                if(MultiplayerManager.debugMode) MelonLogger.Msg("Error: Lobby Creation Failed");
                return;
            }

            lobbyID = (CSteamID)callback.m_ulSteamIDLobby;
            lobbyName = ((ulong)lobbyID % 10000).ToString("D4");
            lobbyIDObj.GetComponent<TextMeshProUGUI>().text = "LOBBY ID: " + lobbyName;
            
            if(MultiplayerManager.debugMode) MelonLogger.Msg("Lobby Created: " + lobbyID);
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
        public static GameObject myHeartsObj = new GameObject("My Hearts", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public static GameObject foeHeartsObj = new GameObject("Foe Hearts", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        public static GameObject overrideWaitingButtonObj = new GameObject("Override Button", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(CursedUI) });
        private static GameObject overrideWaitingButtonTextObj = new GameObject("Override Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        private static List<GameObject> UIObjects = new List<GameObject> { myHeartsObj, foeHeartsObj };

        public static void ToggleOverlay(bool turnOn)
        {
            foreach(GameObject thisObject in UIObjects)
            {
                thisObject.SetActive(turnOn);
            }
        }
        public static void UpdateHearts(int myHearts = -1, int foeHearts = -1)
        {
            try
            {
                if(myHearts == -1) myHearts = CursedNetworking.myPlayerPacket.health;
                if(foeHearts == -1) foeHearts = ReceivedInfo.opponentHealth;

                if(MultiplayerManager.debugMode) MelonLogger.Msg("Health: " + myHearts + " | " + foeHearts);

                string myText = "";
                string foeText = "♥︎♥︎♥︎";
                switch(myHearts)
                {
                    case 3:
                        myText = "<color=red>♥︎♥︎♥︎</color>";
                        break;
                    case 2:
                        myText = "<color=red>♥︎♥</color>♥︎";
                        break;
                    case 1:
                        myText = "<color=red>♥︎</color>♥︎♥︎";
                        break;
                    default:
                        myText = "♥︎♥︎♥︎";
                        break;
                }
                switch(foeHearts)
                {
                    case 3:
                        foeText = "<color=red>♥︎♥︎♥︎</color>";
                        break;
                    case 2:
                        foeText = "<color=red>♥︎♥</color>♥︎";
                        break;
                    case 1:
                        foeText = "<color=red>♥︎</color>♥︎♥︎";
                        break;
                    default:
                        foeText = "♥︎♥︎♥︎";
                        break;
                }

                myHeartsObj.GetComponent<TextMeshProUGUI>().SetText("<font=ShipporiMinchoB1-Bold SDF><color=grey>" + myText + "</color></font>");
                foeHeartsObj.GetComponent<TextMeshProUGUI>().SetText("<font=ShipporiMinchoB1-Bold SDF><color=grey>" + foeText + "</color></font>");
            }
            catch (System.Exception e)
            {
                MelonLogger.Msg(e);
            }
        }
        #endregion
    }

    #region Custom Boss Modifiers
    public class RodmanBoss : BossModifier
    {
        public RodmanBoss()
        {
            this.Name = "Rodman";
            this.PrefabFileName = "Rodman";
            this.AudioPrefix = "Rodman";
            this.SpriteFileName = new WetDennis().GetArtFileName();
            this.UIColor = new WetDennis().GetUIColorA();
            this.DifficultyModifier = new List<int> { 1, 5, 10, 15, 20, 69696969 };
            this.DifficultyIncrease = new List<int> { 0, 0, 0, 0, 0, 0 };
            this.CanBeSummonedByMichael = false;
        }
        public override string GetDescription()
        {
            if(MultiplayerManager.encounterController != null)    
                SetFloorAdjustedModification(GameStatics.GetPlayer().CurrentRunProgress.GetStage() - 1, false);
            return string.Format("All tiles are randomized to be {0}, {1}, or {2}.\nTiles adjacent to {0} or {1} tiles get -{3} BASE SCORE.", new object[]
            {
                "<#D2504D>RED</color>",
                "<#3C83C8>BLUE</color>",
                Tile.ChangeTileTypeToString(TileType.Normal),
                FloorAdjustedModification
            });
        }
        public override Sprite GetBossSprite()
        {
            System.Type type = base.GetType();
            if (!Character.CharacterSprites.ContainsKey(type))
            {
                Sprite value = Resources.Load<Sprite>("Piero/Players/" + this.SpriteFileName + "/" + this.SpriteFileName);
                Character.CharacterSprites[type] = value;
            }
            return Character.CharacterSprites[type];
        }
        public override RuntimeAnimatorController GetRuntimeAnimatorController()
        {
            System.Type type = base.GetType();
            if (!Character.CharacterRuntimeAnimators.ContainsKey(type))
            {
                RuntimeAnimatorController value = Resources.Load<RuntimeAnimatorController>("Piero/Players/" + this.SpriteFileName + "/" + this.SpriteFileName);
                Character.CharacterRuntimeAnimators[type] = value;
            }
            return Character.CharacterRuntimeAnimators[type];
        }
    }
    public class NinaNixBoss : BossModifier
    {
        public NinaNixBoss()
        {
            this.Name = "Nina Nix";
            this.PrefabFileName = "NinaNix";
            this.AudioPrefix = "NinaNix";
            this.SpriteFileName = new NinaNix().GetArtFileName();
            this.UIColor = new NinaNix().GetUIColorA();
            this.DifficultyModifier = new List<int> { 0, 0, 0, 0, 0, 0 };
            this.DifficultyIncrease = new List<int> { 0, 0, 0, 0, 0, 0 };
            this.CanBeSummonedByMichael = false;
        }
        public override string GetDescription()
        {
            return "For each tile in your word get ×-0.95 WORD SCORE";
        }
        public override Sprite GetBossSprite()
        {
            System.Type type = base.GetType();
            if (!Character.CharacterSprites.ContainsKey(type))
            {
                Sprite value = Resources.Load<Sprite>("Piero/Players/" + this.SpriteFileName + "/" + this.SpriteFileName);
                Character.CharacterSprites[type] = value;
            }
            return Character.CharacterSprites[type];
        }
        public override RuntimeAnimatorController GetRuntimeAnimatorController()
        {
            System.Type type = base.GetType();
            if (!Character.CharacterRuntimeAnimators.ContainsKey(type))
            {
                RuntimeAnimatorController value = Resources.Load<RuntimeAnimatorController>("Piero/Players/" + this.SpriteFileName + "/" + this.SpriteFileName);
                Character.CharacterRuntimeAnimators[type] = value;
            }
            return Character.CharacterRuntimeAnimators[type];
        }
    }
    public class HayleyBaylesBoss : AddNumbers
    {
        public HayleyBaylesBoss()
        {
            this.Name = "Hayley Bayles";
            this.PrefabFileName = "HayleyBayles";
            this.SpriteFileName = new HayleyBayles().GetArtFileName();
            this.UIColor = new HayleyBayles().GetUIColorA();
            this.CanBeSummonedByMichael = false;
        }
        public override Sprite GetBossSprite()
        {
            System.Type type = base.GetType();
            if (!Character.CharacterSprites.ContainsKey(type))
            {
                Sprite value = Resources.Load<Sprite>("Piero/Players/" + this.SpriteFileName + "/" + this.SpriteFileName);
                Character.CharacterSprites[type] = value;
            }
            return Character.CharacterSprites[type];
        }
        public override RuntimeAnimatorController GetRuntimeAnimatorController()
        {
            System.Type type = base.GetType();
            if (!Character.CharacterRuntimeAnimators.ContainsKey(type))
            {
                RuntimeAnimatorController value = Resources.Load<RuntimeAnimatorController>("Piero/Players/" + this.SpriteFileName + "/" + this.SpriteFileName);
                Character.CharacterRuntimeAnimators[type] = value;
            }
            return Character.CharacterRuntimeAnimators[type];
        }
    }
    public class SamGambitBoss : BossModifier
    {
        public static ChessPiece chessPiece = ChessPiece.Pawn;
        public SamGambitBoss()
        {
            this.Name = "Sam Gambit";
            this.PrefabFileName = "SamGambit";
            this.AudioPrefix = "Whale";
            this.SpriteFileName = new SamGambit().GetArtFileName();
            this.UIColor = new SamGambit().GetUIColorA();
            this.DifficultyModifier = new List<int> { 0, 0, 0, 0, 0, 0 };
            this.DifficultyIncrease = new List<int> { 0, 0, 0, 0, 0, 0 };
            this.CanBeSummonedByMichael = false;
        }
        public override string GetDescription()
        {
            return "Can only move like the selected chess piece (Currently: " + chessPiece.ToString() + ")";
        }
        public override Sprite GetBossSprite()
        {
            System.Type type = base.GetType();
            if (!Character.CharacterSprites.ContainsKey(type))
            {
                Sprite value = Resources.Load<Sprite>("Piero/Players/" + this.SpriteFileName + "/" + this.SpriteFileName);
                Character.CharacterSprites[type] = value;
            }
            return Character.CharacterSprites[type];
        }
        public override RuntimeAnimatorController GetRuntimeAnimatorController()
        {
            System.Type type = base.GetType();
            if (!Character.CharacterRuntimeAnimators.ContainsKey(type))
            {
                RuntimeAnimatorController value = Resources.Load<RuntimeAnimatorController>("Piero/Players/" + this.SpriteFileName + "/" + this.SpriteFileName);
                Character.CharacterRuntimeAnimators[type] = value;
            }
            return Character.CharacterRuntimeAnimators[type];
        }

        public static void RandomizeChessPiece()
        {
            chessPiece = new ChessPiece[] { ChessPiece.Knight, ChessPiece.Bishop, ChessPiece.Rook, ChessPiece.Queen, ChessPiece.King }[Random.Range(0,5)];
        }
    }
    public class BonesBoss : BossModifier
    {
        public static int wordScoreTaken = 0;
        public BonesBoss()
        {
            this.Name = "Bones The Dog";
            this.PrefabFileName = "BonesTheDog";
            this.AudioPrefix = "BonesTheDog";
            this.SpriteFileName = new BonesTheDog().GetArtFileName();
            this.UIColor = new BonesTheDog().GetUIColorA();
            this.DifficultyModifier = new List<int> { 1, 2, 3, 4, 5, 6 };
            this.DifficultyIncrease = new List<int> { 1, 1, 2, 2, 3, 4 };
            this.CanBeSummonedByMichael = false;
        }
        public override string GetDescription()
        {
            return "Get -" + (wordScoreTaken / 2) + " WORD SCORE. Decreased by " + ((float)FloorAdjustedModification / 2f) + " for each tile in your word."; //Stacks for the whole game
        }
        public override Sprite GetBossSprite()
        {
            System.Type type = base.GetType();
            if (!Character.CharacterSprites.ContainsKey(type))
            {
                Sprite value = Resources.Load<Sprite>("Piero/Players/" + this.SpriteFileName + "/" + this.SpriteFileName);
                Character.CharacterSprites[type] = value;
            }
            return Character.CharacterSprites[type];
        }
        public override RuntimeAnimatorController GetRuntimeAnimatorController()
        {
            System.Type type = base.GetType();
            if (!Character.CharacterRuntimeAnimators.ContainsKey(type))
            {
                RuntimeAnimatorController value = Resources.Load<RuntimeAnimatorController>("Piero/Players/" + this.SpriteFileName + "/" + this.SpriteFileName);
                Character.CharacterRuntimeAnimators[type] = value;
            }
            return Character.CharacterRuntimeAnimators[type];
        }
    }
    public class OctaclesBoss : BossModifier
    {
        public OctaclesBoss()
        {
            this.Name = "Ocatcles";
            this.PrefabFileName = "Ocatcles";
            this.AudioPrefix = "Axolotl";
            this.SpriteFileName = new Octacles().GetArtFileName();
            this.UIColor = new Octacles().GetUIColorA();
            this.DifficultyModifier = new List<int> { 1, 1, 2, 3, 4, 5};
            this.DifficultyIncrease = new List<int> { 0, 0, 0, 0, 0, 0};
            this.BannedFloorIndexes = new List<int>();
            this.CanBeSummonedByMichael = false;
        }

        public override string GetDescription()
        {
            if(MultiplayerManager.encounterController != null)    
                SetFloorAdjustedModification(GameStatics.GetPlayer().CurrentRunProgress.GetStage() - 1, false);
            return "All cursed tiles are replaced with 1 random curse type.";
        }
        public override Sprite GetBossSprite()
        {
            System.Type type = base.GetType();
            if (!Character.CharacterSprites.ContainsKey(type))
            {
                Sprite value = Resources.Load<Sprite>("Piero/Players/" + this.SpriteFileName + "/" + this.SpriteFileName);
                Character.CharacterSprites[type] = value;
            }
            return Character.CharacterSprites[type];
        }
        public override RuntimeAnimatorController GetRuntimeAnimatorController()
        {
            System.Type type = base.GetType();
            if (!Character.CharacterRuntimeAnimators.ContainsKey(type))
            {
                RuntimeAnimatorController value = Resources.Load<RuntimeAnimatorController>("Piero/Players/" + this.SpriteFileName + "/" + this.SpriteFileName);
                Character.CharacterRuntimeAnimators[type] = value;
            }
            return Character.CharacterRuntimeAnimators[type];
        }
    }
    public class NatBoss : BossModifier
    {
        public static List<Item> unavailableItemsList = new List<Item>();
        public NatBoss()
        {
            this.Name = "Nat-H4";
            this.PrefabFileName = "Nat-H4";
            this.AudioPrefix = "NatH4";
            this.SpriteFileName = new NathaServo().GetArtFileName();
            this.UIColor = new NathaServo().GetUIColorA();
            this.DifficultyModifier = new List<int> { 1, 1, 2, 2, 3, 3};
            this.DifficultyIncrease = new List<int> { 1, 1, 1, 1, 1, 1};
            this.BannedFloorIndexes = new List<int>();
            this.CanBeSummonedByMichael = false;
        }

        public override string GetDescription()
        {
            if(MultiplayerManager.encounterController != null)    
                SetFloorAdjustedModification(GameStatics.GetPlayer().CurrentRunProgress.GetStage() - 1, false);
            return "Disables " + FloorAdjustedModification + " random " + (FloorAdjustedModification == 1 ? "item" : "items") + " for the grid.";
        }
        public override Sprite GetBossSprite()
        {
            System.Type type = base.GetType();
            if (!Character.CharacterSprites.ContainsKey(type))
            {
                Sprite value = Resources.Load<Sprite>("Piero/Players/" + this.SpriteFileName + "/" + this.SpriteFileName);
                Character.CharacterSprites[type] = value;
            }
            return Character.CharacterSprites[type];
        }
        public override RuntimeAnimatorController GetRuntimeAnimatorController()
        {
            System.Type type = base.GetType();
            if (!Character.CharacterRuntimeAnimators.ContainsKey(type))
            {
                RuntimeAnimatorController value = Resources.Load<RuntimeAnimatorController>("Piero/Players/" + this.SpriteFileName + "/" + this.SpriteFileName);
                Character.CharacterRuntimeAnimators[type] = value;
            }
            return Character.CharacterRuntimeAnimators[type];
        }
    
        public List<Item> GetRandomizedUnavailableItems()
        {
            unavailableItemsList.RemoveAll(item => true);

            Player player = GameStatics.GetPlayer();
            List<Item> inventory = player.GetAllItems();

            for(int i = 0; i < FloorAdjustedModification; i++)
            {
                List<Item> checkableInventory = (from item in inventory where (unavailableItemsList.Count(i => i.GetType() == item.GetType()) == 0 && item.UpgradeableComponents.Count < 2) select item).ToList();
                unavailableItemsList.Add(checkableInventory[Random.Range(0, checkableInventory.Count)]);
            }

            if(MultiplayerManager.debugMode) MelonLogger.Msg("Taken Item(s): " + string.Join(" | ", unavailableItemsList));

			CharacterInfoPanel.SingletonInventoryVisualController.PopulateAll();

            return unavailableItemsList;
        }
    }
    #endregion

    //Challenge For Multiplayer To Be Set To
    public class Multiplayer : ChallengeRun
    {
        public Multiplayer()
        {
            this.ChallengeName = "Multiplayer";
            this.Description = "Play With Other People!";
            this.EliteQuest = true;
        }
    }
}