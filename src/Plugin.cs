/*
"Funny Item Interactions" Mod Ideas:
Volcano Melts frozen/cold Items In hands
Food Items Next To "Frozen/Frigid" items are frozen when they enter the shop
*/

using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using System.IO;

[assembly: MelonInfo(typeof(CWMultiplayer.MultiplayerManager), "Mario Mod", "0.1.0", "Purely_K2")]
[assembly: MelonGame("Buried Things", "Cursed Words")]

namespace CWMultiplayer
{
    public class MultiplayerManager : MelonMod
    {
        #region Melon Stuff
        public override void OnInitializeMelon()
        {
        }
        public override void OnApplicationQuit()
        {
        }
        #endregion
    }
}