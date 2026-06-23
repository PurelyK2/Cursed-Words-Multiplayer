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
using System.IO;
using nickeltin.SDF.Runtime;
using System.Net.NetworkInformation;

[assembly: MelonInfo(typeof(CWMultiplayer.MultiplayerManager), "Multiplayer Mod", "0.1.0", "Purely_K2")]
[assembly: MelonGame("Buried Things", "Cursed Words")]


/// To do:
/// 1. Make Images For Hearts Work (Use Emojis?)
/// 3. Replace boss sprites with foe's character's sprites (for secret characters, use their boss stuff)
/// 7. If you lose a normal round, lose a heart instead of the game (And maybe get some cash)
/// 10. Turn Off Continue Button If In Lobby
/// 13. Make Time Limit From One Boss To The Next (override speedrun timer to do so?)
/// 16. Make Fairies AND Diving Mask Ungettable
/// 17. Add items to affect opponents
/// 18. Make toggle for real time or grid-based updates for visual score during boss rounds (in update)
/// 19. Give Boss Effects For All Characters!

/// 11. Increase Boss Payout To if you won on first grid
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
            TextureLoaderMod.TextureLoadInit();
            CursedNetworking.SetUpNetworking();
            MelonLogger.Msg("Loaded Multiplayer Mod");
            CursedUI.ToggleOverlay(false);
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
        [HarmonyPatch(typeof(BossDraftController), "Start")]
        public static class AutoChooseBoss_Patch
        {
            public static void Postfix(ref BossDraftController __instance, ref BossDraftVisualController ____visualController)
            {
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
                CursedUI.ToggleOverlay(ReceivedInfo.hasOpponent);
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
                if(!ReceivedInfo.hasOpponent) return true;

                try
                {
		            __instance.SetEncounterThreadStage(EncounterThreadStage.ExecutingWordConsequences);
                    CursedUI.ToggleOverlay(true);
                    CursedUI.waitingTextObj.SetActive(true);
                    CursedUI.overrideWaitingButtonObj.SetActive(true);

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
                        if((ReceivedInfo.opponentHighscore.Score == 0 || ReceivedInfo.opponentIsInBoss) && CursedNetworking.myPlayerPacket.highScore.Score > 0) //haven't imageObjecttten to or are in boss (in boss has high score, out of it doesn't)
                        {
                            _ = AsyncronousWaiting(__instance, tiles, words);
                            return false;
                        }
                        else
                        {
                            MelonLogger.Msg("Opponent Is Done, continuing...");
                        }
                        if(CursedNetworking.myPlayerPacket.highScore.Score > 0)
                        {
                            if(CursedNetworking.myPlayerPacket.highScore > ReceivedInfo.opponentHighscore && ReceivedInfo.opponentHealth <= 0)
                            {
                                GameStatics.GetPlayer().CurrentRunProgress.SetStage(GameStatics.GetNumberOfStages());
                                GameStatics.GetPlayer().CurrentRunProgress.CurrentNodeType = NodeType.Boss;
                                GameStatics.GetPlayer().HasFacedUncursedBoss = true;
                            }
                        }
                        MelonLogger.Msg("Deciding Who Won Between " + CursedNetworking.myPlayerPacket.highScore + " and " + ReceivedInfo.opponentHighscore);
                    }
                }
                catch (System.Exception e)
                {
                    MelonLogger.Msg(e);
                }
                CursedUI.overrideWaitingButtonObj.SetActive(false);
                CursedUI.waitingTextObj.SetActive(false);
                CursedUI.ToggleOverlay(false);
                return true;
            }
            public static void Postfix(ref ScorePacket ____remainingTarget, ref int ____totalGridsPerRound, ref int ____remainingGrids, ref EncounterController __instance)
            {
                if(!ReceivedInfo.hasOpponent) return;

                try
                {
                    if (CursedNetworking.myPlayerPacket.inBoss && ____remainingGrids > 0 && ReceivedInfo.hasOpponent)
                    {
                        if(ReceivedInfo.opponentHighscore.Score > 0)
                            ____remainingTarget = new ScorePacket(mostRecentScorePacket.Score + ReceivedInfo.opponentHighscore.Score);
                        else ____remainingTarget = new ScorePacket(mostRecentScorePacket.Score + 1);
                        if(mostRecentScorePacket > CursedNetworking.myPlayerPacket.highScore) CursedNetworking.myPlayerPacket.UpdatePacket(true, mostRecentScorePacket, CursedNetworking.myPlayerPacket.health);
                        else MelonLogger.Msg("Not Highest Score");
                    }
                    else if(CursedNetworking.myPlayerPacket.highScore.Score > 0 && ReceivedInfo.opponentHighscore.Score > 0 && !ReceivedInfo.opponentIsInBoss && !CursedNetworking.myPlayerPacket.inBoss)
                    {
                        if(ReceivedInfo.opponentHighscore > CursedNetworking.myPlayerPacket.highScore)
                        {
                            CursedNetworking.myPlayerPacket.UpdatePacket(CursedNetworking.myPlayerPacket.inBoss, CursedNetworking.myPlayerPacket.highScore, CursedNetworking.myPlayerPacket.health - 1);
                            MelonLogger.Msg("You Lost A Life!\nCurrent Life: " + CursedNetworking.myPlayerPacket.health);
                        }
                        else if(ReceivedInfo.opponentHighscore == CursedNetworking.myPlayerPacket.highScore)
                        {
                            MelonLogger.Msg("You Tied And Both Lose A Life!");
                        }
                        else
                        {
                            MelonLogger.Msg("You Won The Floor!");
                            ReceivedInfo.opponentHealth -= 1;
                        }

                        if(CursedNetworking.myPlayerPacket.health > 0)
                        {
                            MelonLogger.Msg("You are continuing");
                            ____remainingTarget = new ScorePacket(-1);
                            
                            //Boss Money
                            Player player = GameStatics.GetPlayer();
                            int gridsForMoney = ____totalGridsPerRound - 1;

                            player.ChangeMoney(gridsForMoney * 2);
                        }
                        else
                        {
                            MelonLogger.Msg("Game Over! You lose!");
                            ____remainingTarget = new ScorePacket(mostRecentScorePacket.Score + ReceivedInfo.opponentHighscore.Score);
                            ReceivedInfo.ResetInfo();
                            CursedNetworking.myPlayerPacket.ResetPacket();
                        }

                        ReceivedInfo.opponentHighscore = new ScorePacket(0);
                        _ = AsyncReset();
                    }

                    currentRemainingTarget = ____remainingTarget;
                    CursedUI.UpdateHearts(CursedNetworking.myPlayerPacket.health, ReceivedInfo.opponentHealth);
                }
                catch (System.Exception e)
                {
                    MelonLogger.Msg(e);
                }
            }
            private static async Task AsyncReset()
            {
                await Task.Delay(10000);
                
                CursedNetworking.myPlayerPacket.UpdatePacket(false, new ScorePacket(0), CursedNetworking.myPlayerPacket.health);
            }
        }
        #endregion

        #region Sprite Overrides
        [HarmonyPatch(typeof(CharacterSelectController), "TransitionToNextScene")]
        public static class GetActiveCharacter_Patch
        {
            public static void Prefix(ref Character ____activeCharacter)
            {
                if(!ReceivedInfo.hasOpponent) return;

                try
                {
                    CursedNetworking.myPlayerPacket.myCharacterName = ____activeCharacter.GetName();
                    CursedNetworking.myPlayerPacket.UpdatePacket(false, new ScorePacket(0), 3);
                    CursedNetworking.playerDataChanged = true;
                    CursedNetworking.UpdateAndSendPlayerPacket();
                    MelonLogger.Msg("Updated Player Character to: " + ____activeCharacter.GetName());
                }
                catch (System.Exception e)
                {
                    MelonLogger.Msg(e);
                }
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
                        else if(foeCharacterType == typeof(SandySaguaro))
                            bossModifier = new SandySaguaroBoss();
                        else if(foeCharacterType == typeof(Spike))
                            bossModifier = new CretaceousMegBoss();
                        else if(foeCharacterType == typeof(SockHead))
                            bossModifier = new HumanBoyBoss();
                        else if(foeCharacterType == typeof(PrismaticBean))
                            bossModifier = new PrismaticBeanBoss();
                        else MelonLogger.Msg("Option Is An Invalid Character");
                    }

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
                __result = new List<DiscussionPacket>();
                if (__instance.StolenItem != null)
                {
                    Debug.Log("returning item to player" + __instance.StolenItem.Name);
                    GameStatics.GetPlayer().AddItemToInventory(__instance.StolenItem);
                    CharacterInfoPanel.SingletonInventoryVisualController.PopulateAll();
                    CharacterInfoPanel.SingletonInventoryVisualController.RefreshInspect();
                }
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
        public static class NopeTheOtherWay_Patch
        {
            public static void Postfix(ref Animator ____portraitAnimator)
            {
                if(ReceivedInfo.foeCharacter != null && new List<System.Type> { typeof(WetDennis), typeof(NinaNix), typeof(HayleyBayles), typeof(SamGambit), typeof(BonesTheDog), typeof(Octacles) }.Contains(ReceivedInfo.foeCharacter.GetType()))
                {
                    ____portraitAnimator.gameObject.GetComponent<RectTransform>().localScale = new Vector3(-1f, 1f, 1f);

                    Vector3 PAPos = ____portraitAnimator.gameObject.GetComponent<RectTransform>().localPosition;
                    ____portraitAnimator.gameObject.GetComponent<RectTransform>().localPosition = new Vector3(PAPos.x - 50f, PAPos.y, PAPos.z);
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
                CursedUI.ToggleOverlay(false);
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
                CursedUI.ToggleOverlay(ReceivedInfo.hasOpponent);
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
        #endregion
        
        #region Other Stuff
        private static Vector2 resolution = new Vector2(Screen.width, Screen.height);
        public override void OnUpdate()
        {
            base.OnUpdate();
            if(CursedNetworking.myPlayerPacket.highScore.Score > 0 && ReceivedInfo.opponentHighscore.Score > 0)
            {
                encounterSummaryDisplayController.UpdateDisplayedTargetValue(ReceivedInfo.opponentHighscore, ReceivedInfo.opponentHighscore, false);
            }
            if(SteamAPI.Init()) SteamAPI.RunCallbacks();
            if(CursedUI.lobbyID != CSteamID.Nil) CursedNetworking.UpdateAndSendPlayerPacket();
            if(SteamMatchmaking.GetNumLobbyMembers(CursedUI.lobbyID) > 1 && !ReceivedInfo.hasOpponent)
            {
                ReceivedInfo.hasOpponent = true;
                MelonLogger.Msg("2 People In Lobby!");
            }
            if(SteamMatchmaking.GetNumLobbyMembers(CursedUI.lobbyID) == 1 && ReceivedInfo.hasOpponent)
            {
                ReceivedInfo.hasOpponent = false;
                MelonLogger.Msg("Opponent Disconnected");
                ReceivedInfo.ResetInfo();
                CursedNetworking.myPlayerPacket.ResetPacket();
            }

            //Resolution Stuff
            if(resolution.x != Screen.width || resolution.y != Screen.height)
            {
                resolution.x = Screen.width;
                resolution.y = Screen.height;
                CursedUI.SetUpUIAppearance();
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

        #region Boss Effects
        //Grid
        [HarmonyPatch(typeof(GridUtilitySingleton), "MakeStartOfGridBossAdjustments", new System.Type[] { typeof(GridData), typeof(List<BossModifier>), typeof(ChallengeRun), typeof(List<BoardGenVizInfo>), typeof(int), typeof(bool), typeof(bool)})]
        public static class CustomBossModifiers_Patch
        {
            public static void Prefix(ref List<BossModifier> bossModifiers, ref GridData gridData, ref List<BoardGenVizInfo> vizSteps, ref GridUtilitySingleton __instance)
            {
                //Rodman
                if(bossModifiers.Select(t => t.GetType()).Contains(typeof(RodmanBoss)))
                {
                    MelonLogger.Msg("Fighting Rodman");
                    List<Tile> list = new List<Tile>();
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
                }

                //Hayley Bayles (Literally Just AddNumbers)
                if(bossModifiers.Select(t => t.GetType()).Contains(typeof(HayleyBaylesBoss)))
                {
                    MelonLogger.Msg("Fighting Hayley");
                }

                //Sam Gambit (Like Sicilian Defense)
                if(bossModifiers.Select(t => t.GetType()).Contains(typeof(SamGambitBoss)))
                {
                    ChessPiece[] chessPieces = new ChessPiece[] { ChessPiece.Knight, ChessPiece.Bishop, ChessPiece.Rook, ChessPiece.Queen, ChessPiece.King};
                    SamGambitBoss.chessPiece = chessPieces[Random.Range(0, chessPieces.Length)];
                }

                //Bones The Dog
                if(bossModifiers.Select(t => t.GetType()).Contains(typeof(BonesBoss)))
                {
                }

                //Octacles
                if(bossModifiers.Select(t => t.GetType()).Contains(typeof(Octacles)))
                {
                }

                //Nat-H4
                if(bossModifiers.Select(t => t.GetType()).Contains(typeof(NatBoss)))
                {
                }
            }
        }
        //Score
        [HarmonyPatch(typeof(ScoreCalculation), "ApplyBossModifier", new System.Type[] { typeof(List<TileSelection>), typeof(List<ScoreCalcVizInfo>), typeof(BossModifier) })]
        public static class BossScoreModifiers_Patch
        {
            public static void Postfix(ref ScoreCalcVizInfo __result, ref BossModifier bossModifier, ref List<TileSelection> tiles)
            {
                if(bossModifier is NinaNixBoss)
                {
                    MelonLogger.Msg("Vs Nina");
                    float num = 1f;
                    List<Tile> theseTiles = tiles.Select(tileSelection => tileSelection.SelectedTile).ToList();
                    foreach (Tile tile in theseTiles)
                    {
                        num *= -0.95f;
                    }
                    __result.WordBonus = new WordBonusToken((long)Mathf.RoundToInt(num * 100f), true, false);
                    __result.LettersInWordToPulse.AddRange(theseTiles);
                }
            }
        }
        //Sam Gambit's Gambit
        [HarmonyPatch(typeof(GridUtilitySingleton), "GetValidNextTiles", new System.Type[] { typeof(GridData), typeof(List<Tile>), typeof(TileSelectionManager), typeof(bool) })]
        public static class TheGambit_Patch
        {
            public static void Postfix(ref GridUtilitySingleton __instance, ref List<TileSelection> __result, ref bool noInventory, ref GridData gridData, ref TileSelectionManager tileSelectionManager, ref List<Tile> currentTiles)
            {
                if(!ReceivedInfo.hasOpponent || !encounterController.GetBossModifiers().Select(t => t.GetType()).Contains(typeof(SamGambitBoss))) return;

                List<TileSelection> validTiles = __result;

                //Normal Piece Change
                if(currentTiles[currentTiles.Count - 1].MyGlyphType != GlyphType.Chess)
                {
                    //Remove adjacent tiles (RemoveAll?)
                    //Add tiles for chess moves (GetValidChessMoves?)
                    //Remove Duplicates (MakeUnique?)
                }
            }
        }
        #endregion
    }
    public static class ReceivedInfo
    {
        public static bool hasOpponent = true;
        public static bool opponentIsInBoss = false;
        public static ScorePacket opponentHighscore = new ScorePacket(1);
        public static int opponentHealth = 3;
        public static Character foeCharacter = new SamGambit();

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
            MelonLogger.Msg("Error: Couldn't Find Character");
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
            public ScorePacket highScore;
            public int health;
            public string myCharacterName;
            public PlayerPacket(string name, int totHealth)
            {
                playerName = name;
                inBoss = false;
                highScore = new ScorePacket(0);
                health = totHealth;
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

            MelonLogger.Msg("Steam Linked!");

            myPlayerPacket = new PlayerPacket("", 3);

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
                return;
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
                else if(!(tempLobbyDataList.Contains("Player 1") || tempLobbyDataList.Contains("Player 2")))
                {
                    MelonLogger.Msg("Error In Data, Data Found: " + string.Join(" | ", tempLobbyDataList));
                }
            }

            if(long.TryParse(lobbyDataList[2], out long highScoreLong) && int.TryParse(lobbyDataList[3], out int health))
            {
                if(lobbyDataList[0] != myPlayerPacket.playerName)
                {
                    ReceivedInfo.opponentIsInBoss = lobbyDataList[1] == "True";
                    ReceivedInfo.opponentHighscore = new ScorePacket(highScoreLong);
                    ReceivedInfo.opponentHealth = health;
                    MelonLogger.Msg("Received Info: " + string.Join(" | ", lobbyDataList));
                    if(!ReceivedInfo.hasOpponent)
                    {
                        ReceivedInfo.hasOpponent = true;
                        MelonLogger.Msg("You Now Have An Opponent!");
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
                    MelonLogger.Msg("You Updated Info To: " + string.Join(" | ", lobbyDataList));
                }
            }
            else if(lobbyDataList.Count() == 4)
            {
                MelonLogger.Msg("Failed To Update Player Packet Info - Ints Didn't Parse: " + string.Join(" | ", lobbyDataList));
            }
        }
    }
    public class CursedUI //MADE IT SO HEALTH BAR (TOGGLE OVERLAY) DOESN'T SHOW!!!
    {
        #region GameObjects
        public static GameObject canvasObj = new GameObject("Canvas", new System.Type[] { typeof(Canvas), typeof(RectTransform), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CursedUI), typeof(UnityEngine.UI.Image) });
        private static GameObject eventSystemObj = new GameObject("EventSystem", new System.Type[] { typeof(Transform), typeof(EventSystem), typeof(InputSystemUIInputModule), typeof(CursedUI) });
        private static GameObject lobbyMenuObj = new GameObject("Lobbies Menu", new System.Type[] { typeof(RectTransform), typeof(CursedUI) });
        private static GameObject scrollViewObj = new GameObject("Scorll View", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(CursedUI) });
        public static GameObject showLobbyButtonObj = new GameObject("Show Lobby Button", new System.Type[] {typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(TextMeshProUGUI), typeof(CursedUI) });
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
        
        private static GameObject showLobbyButtonTextObj = new GameObject("Show Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        private static GameObject hideLobbyButtonTextObj = new GameObject("Hide Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        private static GameObject hostLobbyButtonTextObj = new GameObject("Host Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        private static GameObject lobbyButtonTextObj = new GameObject("Lobby Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        private static GameObject joinLobbyButtonTextObj = new GameObject("Join Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        private static GameObject backLobbyButtonTextObj = new GameObject("Back Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
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
                //In-Game UI
                myHeartsObj.transform.SetParent(canvasObj.transform);
                foeHeartsObj.transform.SetParent(canvasObj.transform);
                overrideWaitingButtonObj.transform.SetParent(canvasObj.transform);
                overrideWaitingButtonTextObj.transform.SetParent(overrideWaitingButtonObj.transform);

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
            
            TextMeshProUGUI overrideWaitingButtonText = overrideWaitingButtonTextObj.GetComponent<TextMeshProUGUI>();
            if(overrideWaitingButtonText != null)
            {
                overrideWaitingButtonText.text = "Override";
                overrideWaitingButtonText.color = Color.black;
                overrideWaitingButtonText.fontSize = 14;
                overrideWaitingButtonText.alignment = TextAlignmentOptions.Center;
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
            #region In-Game UI
            RectTransform myHeartsRect = myHeartsObj.GetComponent<RectTransform>();
            if(myHeartsRect != null)
            {
                myHeartsRect.localPosition = new Vector3(7.5f * Screen.width / 19, Screen.height / 2 - 51, 0);
                myHeartsRect.sizeDelta = new Vector2(200, 50);
            }
            RectTransform foeHeartsRect = foeHeartsObj.GetComponent<RectTransform>();
            if(foeHeartsRect != null)
            {
                foeHeartsRect.localPosition = new Vector3(-7.5f * Screen.width / 19, Screen.height / 2 - 51, 0);
                foeHeartsRect.sizeDelta = new Vector2(200, 50);
            }
            UpdateHearts(0, 0);
            overrideWaitingButtonObj.SetActive(false);
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
            Button overrideWaitingTextButton = overrideWaitingButtonObj.GetComponent<Button>();
            if(overrideWaitingTextButton != null)
            {
                overrideWaitingTextButton.onClick.AddListener(OverrideWaiting);
            }
            #endregion
            
            #region Steam Callbacks
            m_lobbyMatchList = Callback<LobbyMatchList_t>.Create(OnLobbyMatchList);
            m_lobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            m_updateData = Callback<LobbyDataUpdate_t>.Create(CursedNetworking.ReceiveAndUpdateFoeInfo);
            m_lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            #endregion
        
            ToggleOverlay(true);
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

            CursedNetworking.myPlayerPacket.playerName = "Player 2";
            MelonLogger.Msg("You Are Player 2");

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
        public static GameObject myHeartsObj = new GameObject("My Hearts", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(Image) });
        public static GameObject foeHeartsObj = new GameObject("Foe Hearts", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(Image) });
        public static GameObject overrideWaitingButtonObj = new GameObject("Override Button", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image), typeof(Button), typeof(CursedUI) });
        private static GameObject overrideWaitingButtonTextObj = new GameObject("Override Text", new System.Type[] { typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI) });
        private static List<GameObject> UIObjects = new List<GameObject> { myHeartsObj, foeHeartsObj };

        public static void ToggleOverlay(bool turnOn)
        {
            foreach(GameObject thisObject in UIObjects)
            {
                thisObject.SetActive(false);
            }
        }
        public static void UpdateHearts(int myHearts, int foeHearts)
        {
            try
            {
                switch(myHearts)
                {
                    case 0:
                        myHeartsObj.GetComponent<Image>().sprite = TextureLoaderMod.noHeartsSprite;
                        break;
                    case 1:
                        myHeartsObj.GetComponent<Image>().sprite = TextureLoaderMod.oneHeartSprite;
                        break;
                    case 2:
                        myHeartsObj.GetComponent<Image>().sprite = TextureLoaderMod.twoHeartsSprite;
                        break;
                    case 3:
                        myHeartsObj.GetComponent<Image>().sprite = TextureLoaderMod.threeHeartsSprite;
                        break;
                    default:
                        MelonLogger.Msg("Error: Invalid Number Of Hearts for Me");
                        return;
                }
                switch(foeHearts)
                {
                    case 0:
                        foeHeartsObj.GetComponent<Image>().sprite = TextureLoaderMod.noHeartsSprite;
                        break;
                    case 1:
                        foeHeartsObj.GetComponent<Image>().sprite = TextureLoaderMod.oneHeartSprite;
                        break;
                    case 2:
                        foeHeartsObj.GetComponent<Image>().sprite = TextureLoaderMod.twoHeartsSprite;
                        break;
                    case 3:
                        foeHeartsObj.GetComponent<Image>().sprite = TextureLoaderMod.threeHeartsSprite;
                        break;
                    default:
                        MelonLogger.Msg("Error: Invalid Number Of Hearts for Foe");
                        return;
                }

                if(myHeartsObj.GetComponent<Image>().sprite == null)
                {
                    myHeartsObj.GetComponent<Image>().color = new UnityEngine.Color((float)CursedNetworking.myPlayerPacket.health / 3f, 0, 0);
                }
                if(foeHeartsObj.GetComponent<Image>().sprite == null)
                {
                    foeHeartsObj.GetComponent<Image>().color = new UnityEngine.Color((float)ReceivedInfo.opponentHealth / 3f, 0, 0);
                }
            }
            catch (System.Exception e)
            {
                MelonLogger.Msg(e);
            }
        }
        #endregion
    }
    public class TextureLoaderMod : MelonMod
    {
        public static Sprite noHeartsSprite;
        public static Sprite oneHeartSprite;
        public static Sprite twoHeartsSprite;
        public static Sprite threeHeartsSprite;
        public static void TextureLoadInit()
        {
            try
            {
                string dllFolder = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if(string.IsNullOrEmpty(dllFolder))
                {
                    MelonLogger.Msg("TextureLoaderMod: Unable to resolve DLL folder.");
                    return;
                }

                noHeartsSprite = LoadSpriteFromFolder("noHearts", dllFolder);
                oneHeartSprite = LoadSpriteFromFolder("oneHeart", dllFolder);
                twoHeartsSprite = LoadSpriteFromFolder("twoHearts", dllFolder);
                threeHeartsSprite = LoadSpriteFromFolder("threeHearts", dllFolder);
            }
            catch(System.Exception e)
            {
                MelonLogger.Msg("TextureLoaderMod: " + e);
            }
        }

        public static Sprite LoadSpriteFromFolder(string baseName, string folder)
        {
            string[] extensions = new[] { ".png", ".jpg", ".jpeg" };
            foreach(string ext in extensions)
            {
                string path = Path.Combine(folder, baseName + ext);
                if(File.Exists(path))
                    return LoadSpriteFromFile(path);
            }

            string fallback = Directory.EnumerateFiles(folder)
                .FirstOrDefault(file => Path.GetFileNameWithoutExtension(file).Equals(baseName, System.StringComparison.OrdinalIgnoreCase));
            if(!string.IsNullOrEmpty(fallback))
                return LoadSpriteFromFile(fallback);

            return null;
        }

        private static Sprite LoadSpriteFromFile(string filePath)
        {
            try
            {
                byte[] imageData = File.ReadAllBytes(filePath);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

                var loadMethod = typeof(Texture2D).GetMethod("LoadImage", new[] { typeof(byte[]) });
                if(loadMethod == null)
                {
                    MelonLogger.Msg("TextureLoaderMod: Texture2D.LoadImage is unavailable.");
                    return null;
                }

                bool loaded = (bool)loadMethod.Invoke(texture, new object[] { imageData });
                if(!loaded)
                {
                    MelonLogger.Msg("TextureLoaderMod: Failed to load image bytes for " + filePath);
                    return null;
                }

                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = Path.GetFileNameWithoutExtension(filePath);
                return sprite;
            }
            catch (System.Exception e)
            {
                MelonLogger.Msg("Error Loading Sprite From File: " + filePath + " -> " + e);
                return null;
            }
        }
    }
    
    #region Custom Boss Modifiers For Other Character Sprites
    public class RodmanBoss : BossModifier
    {
        public RodmanBoss()
        {
            this.Name = "Rodman";
            this.PrefabFileName = "Rodman";
            this.AudioPrefix = "Rodman";
            this.SpriteFileName = new WetDennis().GetArtFileName();
            this.UIColor = new WetDennis().GetUIColorA();
            this.DifficultyModifier = new List<int> { 0, 0, 0, 0, 0, 0 };
            this.DifficultyIncrease = new List<int> { 0, 0, 0, 0, 0, 0 };
            this.CanBeSummonedByMichael = false;
        }
        public override string GetDescription()
        {
            return "Trichromatic: All Tiles Are Randomized To Be Red, Blue, Or Normal";
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
            return "Gambit: Can Only Move Like The Selected Chess Piece (Currently: " + chessPiece.ToString() + ")";
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
    public class BonesBoss : BossModifier
    {
        public BonesBoss()
        {
            this.Name = "Bones The Dog";
            this.PrefabFileName = "BonesTheDog";
            this.AudioPrefix = "BonesTheDog";
            this.SpriteFileName = new BonesTheDog().GetArtFileName();
            this.UIColor = new BonesTheDog().GetUIColorA();
            this.DifficultyModifier = new List<int> { 0, 0, 0, 0, 0, 0 };
            this.DifficultyIncrease = new List<int> { 0, 0, 0, 0, 0, 0 };
            this.CanBeSummonedByMichael = false;
        }
        public override string GetDescription()
        {
            return "Gamba: -0 WORD SCORE. Decreased by X for each tile used in your word."; //Stacks for the whole game
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
            this.DifficultyModifier = new List<int> { 0, 0, 0, 0, 0, 0};
            this.DifficultyIncrease = new List<int> { 0, 0, 0, 0, 0, 0};
            this.BannedFloorIndexes = new List<int>();
            this.CanBeSummonedByMichael = false;
        }

        public override string GetDescription()
        {
            return "Clueless: All Non-Cursed Tiles Score 0";
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
        public NatBoss()
        {
            this.Name = "Nat-H4";
            this.PrefabFileName = "Nat-H4";
            this.AudioPrefix = "NatH4";
            this.SpriteFileName = new NathaServo().GetArtFileName();
            this.UIColor = new NathaServo().GetUIColorA();
            this.DifficultyModifier = new List<int> { 0, 0, 0, 0, 0, 0};
            this.DifficultyIncrease = new List<int> { 0, 0, 0, 0, 0, 0};
            this.BannedFloorIndexes = new List<int>();
            this.CanBeSummonedByMichael = false;
        }

        public override string GetDescription()
        {
            return "Disables X Random Item(s) Each Round";
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
    //Sandy Saguaro:
    //Cretaceous Meg: 
    //Beans: 
    #endregion
}