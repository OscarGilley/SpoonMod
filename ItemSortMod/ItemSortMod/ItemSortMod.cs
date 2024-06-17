using BepInEx;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using R2API;
using Rewired;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RiskOfOptions;
using UnityEngine;
using UnityEngine.AddressableAssets;
using RiskOfOptions.Options;
using BepInEx.Configuration;
using R2API.Utils;
using RiskOfOptions.OptionConfigs;

namespace ItemSortMod
{
    [BepInDependency(ItemAPI.PluginGUID)]

    [BepInDependency("com.rune580.riskofoptions")]

    // This one is because we use a .language file for language tokens
    // More info in https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/Assets/Localization/
    [BepInDependency(LanguageAPI.PluginGUID)]

    // This attribute is required, and lists metadata for your plugin.
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class ItemSortMod : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "OscarGilley";
        public const string PluginName = "ItemSortingMod";
        public const string PluginVersion = "1.0.0";

        private static Hook inventoryHook;
        private static Hook scrapperHook;
        RoR2.UI.ItemInventoryDisplay items;

        ConfigEntry<bool> SortByStack;
        ConfigEntry<bool> StackDescending;
        ConfigEntry<bool> SortAlphabetical;
        ConfigEntry<bool> AlphabeticalDescending;
        ConfigEntry<bool> SortByTier;


        // The Awake() method is run at the very start when the game is initialized.
        public void Awake()
        {
            // Init our logging class so that we can properly log for debugging
            Log.Init(Logger);

            SortByStack = Config.Bind("General", "Sort By Stack", true, "Sorts items by stack in your inventory.");
            SortByStack.SettingChanged += SettingsChanged;
            ModSettingsManager.AddOption(new CheckBoxOption(SortByStack, new CheckBoxConfig { name = "Sort By Stack" } ));

            StackDescending = Config.Bind("General", "Stack Descending", false, "Groups stacks by decending value (lowest to highest).");
            StackDescending.SettingChanged += SettingsChanged;
            ModSettingsManager.AddOption(new CheckBoxOption(StackDescending, new CheckBoxConfig() { name = "Sort Stacks Descending", checkIfDisabled = () => { return !SortByStack.Value; } }));

            SortAlphabetical = Config.Bind("General", "Sort Alphabetical", true, "Sorts items alphabetically in your inventory.");
            SortAlphabetical.SettingChanged += SettingsChanged;
            ModSettingsManager.AddOption(new CheckBoxOption(SortAlphabetical, new CheckBoxConfig() { name = "Sort Alphabetically" }));

            AlphabeticalDescending = Config.Bind("General", "Alphabetical Descending", false, "Sorts inventory by descending alphabetical (Z-A).");
            AlphabeticalDescending.SettingChanged += SettingsChanged;
            ModSettingsManager.AddOption(new CheckBoxOption(AlphabeticalDescending, new CheckBoxConfig() { name = "Sort Alphabetical Descending", checkIfDisabled = () => { return !SortAlphabetical.Value; } }));

            SortByTier = Config.Bind("General", "Sort By Tier", true, "Groups items by tier in your inventory.");
            SortByTier.SettingChanged += SettingsChanged;
            ModSettingsManager.AddOption(new CheckBoxOption(SortByTier, new CheckBoxConfig() { name = "Sort By Tier" }));

            InitHooks();
        }

        void InitHooks()
        {
            var inventoryTarget = typeof(RoR2.UI.ItemInventoryDisplay).GetMethod(nameof(RoR2.UI.ItemInventoryDisplay.UpdateDisplay), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var inventoryDest = typeof(ItemSortMod).GetMethod(nameof(UpdateDisplayOverride), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var scrapperTarget = typeof(RoR2.PickupPickerController).GetMethod(nameof(RoR2.PickupPickerController.OnDisplayBegin), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var scrapperDest = typeof(ItemSortMod).GetMethod(nameof(OnDisplayBeginOverride), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            inventoryHook = new Hook(inventoryTarget, inventoryDest, this);
            scrapperHook = new Hook(scrapperTarget, scrapperDest, this);
        }

        private void SettingsChanged(object sender, EventArgs args)
        {
            if (items)
            {
                items.UpdateDisplay();
            }
        }

        private void UpdateDisplayOverride(Action<RoR2.UI.ItemInventoryDisplay> orig, RoR2.UI.ItemInventoryDisplay self)
        {
            items = self;
            var tempOrder = self.itemOrder;
            ItemIndex[] sortedArray = sortItems(self.itemOrder, self.itemOrderCount, self.itemStacks);
            self.itemOrder = sortedArray;
            orig(self);
        }

        private void OnDisplayBeginOverride(Action<RoR2.PickupPickerController, NetworkUIPromptController, LocalUser, CameraRigController> orig, RoR2.PickupPickerController self, NetworkUIPromptController networkUIPromptController, LocalUser localUser, CameraRigController cameraRigController)
        {
            orig(self, networkUIPromptController, localUser, cameraRigController);
            if (self.options.Length > 0) 
            {
                if (PickupCatalog.GetPickupDef(self.options[0].pickupIndex).itemIndex == (ItemIndex)(-1))
                {
                    EquipmentIndex[] equipmentArray = new EquipmentIndex[self.options.Length];
                    Dictionary<EquipmentIndex, GameObject> dictionary = new Dictionary<EquipmentIndex, GameObject>();
                    for (int i = 0; i < self.options.Length; i++)
                    {
                        EquipmentIndex equipmentIndex = PickupCatalog.GetPickupDef(self.options[i].pickupIndex).equipmentIndex;
                        dictionary.Add(equipmentIndex, self.panelInstanceController.buttonAllocator.elements[i].gameObject);
                        equipmentArray[i] = equipmentIndex;
                    }
                    equipmentArray = sortEquipment(equipmentArray, self.options.Length);
                    foreach (var equipment in equipmentArray)
                    {
                        dictionary[equipment].transform.SetAsLastSibling();
                    }
                }
                else
                {
                    ItemIndex[] itemArray = new ItemIndex[self.options.Length];
                    Dictionary<ItemIndex, GameObject> dictionary = new Dictionary<ItemIndex, GameObject>();
                    for (int i = 0; i < self.options.Length; i++)
                    {
                        Debug.Log(self.panelInstanceController.buttonAllocator.elements);
                        ItemIndex itemIndex = PickupCatalog.GetPickupDef(self.options[i].pickupIndex).itemIndex;
                        Debug.Log("aaaaa");
                        dictionary.Add(itemIndex, self.panelInstanceController.buttonAllocator.elements[i].gameObject);
                        itemArray[i] = itemIndex;
                    }
                    itemArray = sortItems(itemArray, self.options.Length, localUser.cachedBody.inventory.itemStacks);
                    foreach (var item in itemArray)
                    {
                        dictionary[item].transform.SetAsLastSibling();
                    }
                }
            }
        }

        private void AddFireworks(Action<RoR2.UI.ItemInventoryDisplay> orig, RoR2.UI.ItemInventoryDisplay self)
        {
            items = self;
            self.inventory.GiveItem((ItemIndex)42);

            orig(self);
        }

        //old way without tiers, repurposed for equipment
        private EquipmentIndex[] sortEquipment(EquipmentIndex[] itemOrder, int itemOrderCount)
        {
            if (SortAlphabetical.Value)
            {
                for (int j = 0; j < itemOrderCount; j++)
                {
                    Array.Sort(itemOrder, (x, y) => string.Compare(Language.GetString(EquipmentCatalog.GetEquipmentDef(x).nameToken), Language.GetString(EquipmentCatalog.GetEquipmentDef(y).nameToken)));
                }
            }
            return itemOrder;
        }
        
        private ItemIndex[] sortItems(ItemIndex[] itemOrder, int itemOrderCount, int[] itemStacks)
        {
            int tierLength = RoR2.ContentManagement.ContentManager.itemTierDefs.Length;
            List<ItemIndex>[] itemTiers = new List<ItemIndex>[tierLength]; // array of item index lists of length total number of tiers

            // initialise lists
            for (int k = 0; k < tierLength; k++)
            {
                itemTiers[k] = new List<ItemIndex>();
            }

            if (SortByTier.Value)
            {
                // List<ItemIndex> noTierItems = new List<ItemIndex>(); //shitlist

                // sort within tiers
                for (int i = 0; i < itemOrderCount; i++)
                {
                    int temp = (int)ItemCatalog.GetItemDef(itemOrder[i]).tier; // find out the tier of current item
                    /* exception for the noTierItems, so they can be added at the end
                    if (temp == (int)ItemTier.NoTier)
                    {
                        noTierItems.Add(itemOrder[i]);
                    }
                    */
                    itemTiers[temp].Add(itemOrder[i]); // put that item in the correct list      
                }
            }
            // don't care about tiers, just put everything in the first index of itemTiers
            // i assume that there will never be 0 item tiers, otherwise this would error
            // but there's probably more problems with ror2 if there's no item tiers...
            else
            {
                for (int i = 0; i < itemOrderCount; i++)
                {
                    itemTiers[0].Add(itemOrder[i]); // put that item in the first list
                }
            }
            
            int count = 0;

            // then sort the lists and push to itemOrder
            ItemIndex[] sortedArray = new ItemIndex[itemOrder.Length];

            for (int j = 0; j < itemTiers.Count(); j++)
            {
                // don't sort empty lists
                if (itemTiers[j].Count() > 0)
                {
                    List<ItemIndex> list = itemTiers[j];
                    if (SortByStack.Value)
                    {
                        IOrderedEnumerable<ItemIndex> orderedList;
                        // have to sort by stack then alphabetical, otherwise it's just alphabetical
                        if (SortAlphabetical.Value)
                        {
                            if (AlphabeticalDescending.Value)
                            {
                                orderedList = itemTiers[j].OrderBy(i => (StackDescending.Value ? 1 : -1) * itemStacks[(int)i]).ThenByDescending(i => Language.GetString(ItemCatalog.GetItemDef(i).nameToken));
                            }
                            else
                            {
                                orderedList = itemTiers[j].OrderBy(i => (StackDescending.Value ? 1 : -1) * itemStacks[(int)i]).ThenBy(i => Language.GetString(ItemCatalog.GetItemDef(i).nameToken));
                            }
                        }
                        // don't care about alphabetical, just sort by stack size
                        else
                        {
                            orderedList = itemTiers[j].OrderBy(i => (StackDescending.Value ? 1 : -1) * itemStacks[(int)i]);
                        }
                        list = orderedList.ToList();
                    }
                    // don't care about stack size, just sort alphabetical
                    else if (SortAlphabetical.Value)
                    {
                        list.Sort((x, y) => string.Compare(Language.GetString(ItemCatalog.GetItemDef(x).nameToken), Language.GetString(ItemCatalog.GetItemDef(y).nameToken)));
                        // flip the list if descending
                        if (AlphabeticalDescending.Value)
                        {
                            list.Reverse();
                        }
                    }
                    // don't care about alphabet OR stacks, just chuck the current tier in
                    else
                    {
                        list = itemTiers[j];
                    }
                    Debug.Log("sort finished!");
                    foreach (ItemIndex item in list)
                    {
                        // if item tier = notier, put in noTierItems, don't increment count
                        // Debug.Log("next sorted item is:");
                        // Debug.Log(Language.GetString(ItemCatalog.GetItemDef(item).nameToken));
                        // Debug.Log(item);
                        sortedArray[count] = item;
                        count++;
                    }
                }
            }
            /*
            foreach (ItemIndex item in noTierItems)
            {
                sortedList[count] = item.Add(item); //shitlist goes at the end
                count++;
            }
            */
            return sortedArray;
        }

        // The Update() method is run on every frame of the game.
        private void Update()
        {
            // This if statement checks if the player has currently pressed F2.
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                // Get the player body to use a position:
                var transform = PlayerCharacterMasterController.instances[0].master.GetBodyObject().transform;

                // And then drop our defined item in front of the player.

                Log.Info($"Player pressed backspace. Spawning a random test item at coordinates {transform.position}");
                System.Random r = new System.Random();
                int rInt = r.Next(1, 150);
                // PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex((ItemIndex)rInt), transform.position, transform.forward * 20f);
            }
        }
    }
}
