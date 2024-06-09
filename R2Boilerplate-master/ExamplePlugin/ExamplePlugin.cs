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

namespace ExamplePlugin
{
    // This is an example plugin that can be put in
    // BepInEx/plugins/ExamplePlugin/ExamplePlugin.dll to test out.
    // It's a small plugin that adds a relatively simple item to the game,
    // and gives you that item whenever you press F2.

    // This attribute specifies that we have a dependency on a given BepInEx Plugin,
    // We need the R2API ItemAPI dependency because we are using for adding our item to the game.
    // You don't need this if you're not using R2API in your plugin,
    // it's just to tell BepInEx to initialize R2API before this plugin so it's safe to use R2API.
    [BepInDependency(ItemAPI.PluginGUID)]

    [BepInDependency("com.rune580.riskofoptions")]

    // This one is because we use a .language file for language tokens
    // More info in https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/Assets/Localization/
    [BepInDependency(LanguageAPI.PluginGUID)]

    // This attribute is required, and lists metadata for your plugin.
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    // This is the main declaration of our plugin class.
    // BepInEx searches for all classes inheriting from BaseUnityPlugin to initialize on startup.
    // BaseUnityPlugin itself inherits from MonoBehaviour,
    // so you can use this as a reference for what you can declare and use in your plugin class
    // More information in the Unity Docs: https://docs.unity3d.com/ScriptReference/MonoBehaviour.html
    public class ExamplePlugin : BaseUnityPlugin
    {
        // The Plugin GUID should be a unique ID for this plugin,
        // which is human readable (as it is used in places like the config).
        // If we see this PluginGUID as it is on thunderstore,
        // we will deprecate this mod.
        // Change the PluginAuthor and the PluginName !
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "AuthorName";
        public const string PluginName = "ExamplePlugin";
        public const string PluginVersion = "1.0.0";

        // We need our item definition to persist through our functions, and therefore make it a class field.
        private static ItemDef myItemDef;
        private static Hook inventoryHook;
        private static Hook scrapperHook;
        RoR2.UI.ItemInventoryDisplay items;
        RoR2.PickupPickerController scrapperUI;


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

            // First let's define our item
            myItemDef = ScriptableObject.CreateInstance<ItemDef>();

            // Language Tokens, explained there https://risk-of-thunder.github.io/R2Wiki/Mod-Creation/Assets/Localization/
            myItemDef.name = "EXAMPLE_CLOAKONKILL_NAME";
            myItemDef.nameToken = "EXAMPLE_CLOAKONKILL_NAME";
            myItemDef.pickupToken = "EXAMPLE_CLOAKONKILL_PICKUP";
            myItemDef.descriptionToken = "EXAMPLE_CLOAKONKILL_DESC";
            myItemDef.loreToken = "EXAMPLE_CLOAKONKILL_LORE";

            // The tier determines what rarity the item is:
            // Tier1=white, Tier2=green, Tier3=red, Lunar=Lunar, Boss=yellow,
            // and finally NoTier is generally used for helper items, like the tonic affliction
#pragma warning disable Publicizer001 // Accessing a member that was not originally public. Here we ignore this warning because with how this example is setup we are forced to do this
            myItemDef._itemTierDef = Addressables.LoadAssetAsync<ItemTierDef>("RoR2/Base/Common/Tier2Def.asset").WaitForCompletion();
#pragma warning restore Publicizer001
            // Instead of loading the itemtierdef directly, you can also do this like below as a workaround
            // myItemDef.deprecatedTier = ItemTier.Tier2;

            // You can create your own icons and prefabs through assetbundles, but to keep this boilerplate brief, we'll be using question marks.
            myItemDef.pickupIconSprite = Addressables.LoadAssetAsync<Sprite>("RoR2/Base/Common/MiscIcons/texMysteryIcon.png").WaitForCompletion();
            myItemDef.pickupModelPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Mystery/PickupMystery.prefab").WaitForCompletion();

            // Can remove determines
            // if a shrine of order,
            // or a printer can take this item,
            // generally true, except for NoTier items.
            myItemDef.canRemove = true;

            // Hidden means that there will be no pickup notification,
            // and it won't appear in the inventory at the top of the screen.
            // This is useful for certain noTier helper items, such as the DrizzlePlayerHelper.
            myItemDef.hidden = false;

            // You can add your own display rules here,
            // where the first argument passed are the default display rules:
            // the ones used when no specific display rules for a character are found.
            // For this example, we are omitting them,
            // as they are quite a pain to set up without tools like https://thunderstore.io/package/KingEnderBrine/ItemDisplayPlacementHelper/
            var displayRules = new ItemDisplayRuleDict(null);

            // Then finally add it to R2API
            ItemAPI.Add(new CustomItem(myItemDef, displayRules));

            // But now we have defined an item, but it doesn't do anything yet. So we'll need to define that ourselves.
            GlobalEventManager.onCharacterDeathGlobal += GlobalEventManager_onCharacterDeathGlobal;

            //NEW STUFF
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
            var inventoryDest = typeof(ExamplePlugin).GetMethod(nameof(UpdateDisplayOverride), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var scrapperTarget = typeof(RoR2.PickupPickerController).GetMethod(nameof(RoR2.UI.PickupPickerPanel.SetPickupOptions), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var scrapperDest = typeof(ExamplePlugin).GetMethod(nameof(OnDisplayBeginOverride), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            inventoryHook = new Hook(inventoryTarget, inventoryDest, this);
            scrapperHook = new Hook(scrapperTarget, scrapperDest, this);
        }

        private void SettingsChanged(object sender, EventArgs args)
        {
            Debug.Log("guh");
            Debug.Log(items);
            if (items)
            {
                Debug.Log("GUH!!!");
                items.UpdateDisplay();
            }
        }

        private void UpdateDisplayOverride(Action<RoR2.UI.ItemInventoryDisplay> orig, RoR2.UI.ItemInventoryDisplay self)
        {
            items = self;
            var tempOrder = self.itemOrder;

            //old way without tiers
            /*
            for (int j = 0; j < self.itemOrderCount; j++)
            { 
                Array.Sort(self.itemOrder, (x, y) => string.Compare(Language.GetString(ItemCatalog.GetItemDef(x).nameToken), Language.GetString(ItemCatalog.GetItemDef(y).nameToken)));
            }
            */
            ItemIndex[] sortedArray = sortItems(self.itemOrder, self.itemOrderCount, self.itemStacks);
            self.itemOrder = sortedArray;

            orig(self);

            /*
            int tierLength = RoR2.ContentManagement.ContentManager.itemTierDefs.Length;
            List<ItemIndex>[] itemTiers = new List<ItemIndex>[tierLength]; // array of item index lists of length total number of tiers 
            
            //initialise lists
            for (int k = 0; k < tierLength; k++)
            {
                itemTiers[k] = new List<ItemIndex>();
            }
            // List<ItemIndex> noTierItems = new List<ItemIndex>(); //shitlist

            // sort within tiers
            for (int i = 0; i < self.itemOrderCount; i++)
            {
                int temp = (int)ItemCatalog.GetItemDef(self.itemOrder[i]).tier; // find out the tier of current item
                itemTiers[temp].Add(self.itemOrder[i]); // put that item in the correct list      
            }
            // then sort the lists and push to itemOrder
            ItemIndex[] sortedArray = new ItemIndex[self.itemOrder.Length];
            int count = 0;
            bool sortByStack = true;
            bool descendingAlphabetical = true;
            bool descendingStack = false;
            for (int j = 0; j < itemTiers.Count(); j++)
            {
                // don't sort empty lists
                if (itemTiers[j].Count() > 0)
                {
                    List<ItemIndex> list = itemTiers[j];
                    if (sortByStack)
                    {
                        IOrderedEnumerable<ItemIndex> orderedList;

                        if (descendingAlphabetical)
                        {
                            orderedList = itemTiers[j].OrderBy(i => (descendingStack ? 1 : -1) * self.itemStacks[(int)i]).ThenByDescending(i => Language.GetString(ItemCatalog.GetItemDef(i).nameToken));
                        }
                        else
                        {
                            orderedList = itemTiers[j].OrderBy(i => (descendingStack ? 1 : -1) * self.itemStacks[(int)i]).ThenBy(i => Language.GetString(ItemCatalog.GetItemDef(i).nameToken));
                        }
                        list = orderedList.ToList();
                    }
                    else
                    {
                        list.Sort((x, y) => string.Compare(Language.GetString(ItemCatalog.GetItemDef(x).nameToken), Language.GetString(ItemCatalog.GetItemDef(y).nameToken)));
                    }
                    foreach (ItemIndex item in list)
                    {
                        // if item tier = notier, put in noTierItems, don't increment count
                        // Debug.Log(Language.GetString(ItemCatalog.GetItemDef(item).nameToken));
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

        }

        private void OnDisplayBeginOverride(Action<RoR2.PickupPickerController, NetworkUIPromptController, LocalUser, CameraRigController> orig, RoR2.PickupPickerController self, NetworkUIPromptController networkUIPromptController, LocalUser localUser, CameraRigController cameraRigController)
        {
            Debug.Log(self.options);
            scrapperUI = self;
            System.Type type = typeof(RoR2.NetworkUIPromptController);
            System.Reflection.FieldInfo info = type.GetField("_currentLocalParticipant", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            RoR2.Inventory inventory = localUser.currentNetworkUser.GetCurrentBody().inventory;
            if (self.options.Length > 0) 
            {
                if (PickupCatalog.GetPickupDef(self.options[0].pickupIndex).itemIndex == (ItemIndex)(-1))
                {
                    EquipmentIndex[] equipmentArray = new EquipmentIndex[self.options.Length];
                    for (int i = 0; i < self.options.Length; i++)
                    {
                        equipmentArray[i] = PickupCatalog.GetPickupDef(self.options[i].pickupIndex).equipmentIndex;
                    }
                    equipmentArray = sortEquipment(equipmentArray, self.options.Length);
                    self.options = self.options.OrderBy(x => Array.IndexOf(equipmentArray, PickupCatalog.GetPickupDef(x.pickupIndex).equipmentIndex)).ToArray();
                }
                else
                {
                    ItemIndex[] itemArray = new ItemIndex[self.options.Length];
                    for (int i = 0; i < self.options.Length; i++)
                    {
                        itemArray[i] = PickupCatalog.GetPickupDef(self.options[i].pickupIndex).itemIndex;
                        Debug.Log("hi this item is");
                        Debug.Log(PickupCatalog.GetPickupDef(self.options[i].pickupIndex).nameToken);
                    }
                    itemArray = sortItems(itemArray, self.options.Length, localUser.cachedBody.inventory.itemStacks);
                    // self.options = scrapperUI.options.OrderBy(x => Array.IndexOf(itemArray, PickupCatalog.GetPickupDef(x.pickupIndex).itemIndex)).ToArray();
                    var sorted = scrapperUI.options.OrderBy(x => Array.IndexOf(itemArray, PickupCatalog.GetPickupDef(x.pickupIndex).itemIndex)).ToArray();
                    self.options = sorted;

                    // self.options = scrapperUI.options.OrderBy(x => Array.IndexOf(itemArray, PickupCatalog.GetPickupDef(x.pickupIndex).itemIndex)).ToArray();

                    Debug.Log("sanity check the ordering");
                    for (int i = 0; i < self.options.Length; i++)
                    {
                        Debug.Log(Language.GetString(PickupCatalog.GetPickupDef(self.options[i].pickupIndex).nameToken));
                        Debug.Log(PickupCatalog.GetPickupDef(self.options[i].pickupIndex).itemIndex);
                        Debug.Log("real sorted array");
                        Debug.Log(Language.GetString(ItemCatalog.GetItemDef(itemArray[i]).nameToken));
                        Debug.Log(itemArray[i]);
                    }
                }
            }
            orig(self, networkUIPromptController, localUser, cameraRigController);
        }

        private void AddFireworks(Action<RoR2.UI.ItemInventoryDisplay> orig, RoR2.UI.ItemInventoryDisplay self)
        {
            items = self;
            self.inventory.GiveItem((ItemIndex)42);

            orig(self);
        }

        //old way without tiers
        private EquipmentIndex[] sortEquipment(EquipmentIndex[] itemOrder, int itemOrderCount)
        {
            for (int j = 0; j < itemOrderCount; j++)
            {
                Array.Sort(itemOrder, (x, y) => string.Compare(Language.GetString(EquipmentCatalog.GetEquipmentDef(x).nameToken), Language.GetString(EquipmentCatalog.GetEquipmentDef(y).nameToken)));
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
                    /* exception for the shitlist, so they can be added at the end
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
                        Debug.Log("next sorted item is:");
                        Debug.Log(Language.GetString(ItemCatalog.GetItemDef(item).nameToken));
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
        private void GlobalEventManager_onCharacterDeathGlobal(DamageReport report)
        {
            // If a character was killed by the world, we shouldn't do anything.
            if (!report.attacker || !report.attackerBody)
            {
                return;
            }

            var attackerCharacterBody = report.attackerBody;

            // We need an inventory to do check for our item
            if (attackerCharacterBody.inventory)
            {
                // Store the amount of our item we have
                var garbCount = attackerCharacterBody.inventory.GetItemCount(myItemDef.itemIndex);
                if (garbCount > 0 &&
                    // Roll for our 50% chance.
                    Util.CheckRoll(50, attackerCharacterBody.master))
                {
                    // Since we passed all checks, we now give our attacker the cloaked buff.
                    // Note how we are scaling the buff duration depending on the number of the custom item in our inventory.
                    attackerCharacterBody.AddTimedBuff(RoR2Content.Buffs.Cloak, 3 + garbCount);
                }
            }
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

                Log.Info($"Player pressed F2. Spawning our custom item at coordinates {transform.position}");
                System.Random r = new System.Random();
                int rInt = r.Next(1, 150);
                PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex((ItemIndex)rInt), transform.position, transform.forward * 20f);
            }
        }
    }
}
