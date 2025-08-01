#include "MQ2EZInv_E3Integration.h"
#include "MQ2EZInv.h" // For InventoryData and ItemData structures
#include "MQ2EZInv_SimpleJSON.h" // Use MQ2EZInv's own JSON library
#include <mq/Plugin.h>

// Global instance
std::unique_ptr<E3NextInventoryManager> g_e3InventoryManager;

E3NextInventoryManager::E3NextInventoryManager() 
    : m_lastUpdate(std::chrono::steady_clock::now())
{
}

E3NextInventoryManager::~E3NextInventoryManager() 
{
    Shutdown();
}

bool E3NextInventoryManager::Initialize() 
{
    WriteChatf("[MQ2EZInv] Initializing E3Next integration...");
    
    // Auto-discover E3Next characters
    auto characters = E3Integration::DiscoverE3NextCharacters();
    
    if (characters.empty()) 
    {
        WriteChatf("[MQ2EZInv] No E3Next characters found. You can manually add characters later.");
        return true; // Still return true, we can add characters later
    }
    
    WriteChatf("[MQ2EZInv] Found %d potential E3Next characters", static_cast<int>(characters.size()));
    
    bool anyConnected = false;
    int connectionAttempts = 0;
    const int maxAttempts = 100; // Limit connection attempts to prevent hanging
    
    for (const auto& character : characters) 
    {
        if (connectionAttempts >= maxAttempts) {
            WriteChatf("[MQ2EZInv] Reached maximum connection attempts (%d), stopping", maxAttempts);
            break;
        }
        
        connectionAttempts++;
        
        if (AddCharacter(character)) 
        {
            anyConnected = true;
            WriteChatf("[MQ2EZInv] Connected to E3Next character: %s", character.c_str());
        }
        else 
        {
            // Don't spam the chat with failed connections
            // WriteChatf("[MQ2EZInv] Failed to connect to E3Next character: %s (not running?)", character.c_str());
        }
    }
    
    WriteChatf("[MQ2EZInv] E3Next integration initialized (%s) - %d connection attempts", 
              anyConnected ? "connected" : "no connections", connectionAttempts);
    return true;
}

void E3NextInventoryManager::Shutdown() 
{
    WriteChatf("[MQ2EZInv] Shutting down E3Next integration...");
    m_readers.clear();
    m_characterNames.clear();
    m_cachedInventories.clear();
}

bool E3NextInventoryManager::AddCharacter(const std::string& characterName) 
{
    // Check if already connected
    if (FindCharacterIndex(characterName) != -1) 
    {
        return true;
    }
    
    auto reader = std::make_unique<SharedMemoryReader>(characterName);
    if (reader->Initialize()) 
    {
        m_readers.push_back(std::move(reader));
        m_characterNames.push_back(characterName);
        return true;
    }
    
    return false;
}

void E3NextInventoryManager::RemoveCharacter(const std::string& characterName) 
{
    int index = FindCharacterIndex(characterName);
    if (index != -1) 
    {
        m_readers.erase(m_readers.begin() + index);
        m_characterNames.erase(m_characterNames.begin() + index);
        m_cachedInventories.erase(characterName);
    }
}

void E3NextInventoryManager::Update() 
{
    auto now = std::chrono::steady_clock::now();
    if (now - m_lastUpdate < UPDATE_INTERVAL) 
    {
        return;
    }
    m_lastUpdate = now;

    // Auto-discover and connect to any new E3Next characters
    {
        auto newChars = E3Integration::DiscoverE3NextCharacters();
        for (const auto& charName : newChars) {
            if (FindCharacterIndex(charName) == -1) {
                if (AddCharacter(charName)) {
                    WriteChatf("[MQ2EZInv] Auto-connected to new E3Next character: %s", charName.c_str());
                }
            }
        }
    }

    for (size_t i = 0; i < m_readers.size(); ++i) 
    {
        if (!m_readers[i]->IsInitialized()) 
        {
            // Try to reconnect
            m_readers[i]->Reconnect();
            continue;
        }
        
        std::string jsonData = m_readers[i]->ReadInventoryData();
        if (!jsonData.empty()) 
        {
            WriteChatf("[MQ2EZInv] Read %d bytes from %s", jsonData.length(), m_characterNames[i].c_str());
            ParseAndCacheInventoryData(m_characterNames[i], jsonData);
        }
    }
}

bool E3NextInventoryManager::GetCharacterInventory(const std::string& characterName, InventoryData& inventory) const 
{
    auto it = m_cachedInventories.find(characterName);
    if (it != m_cachedInventories.end()) 
    {
        inventory = it->second;
        return true;
    }
    return false;
}

std::vector<std::string> E3NextInventoryManager::GetConnectedCharacters() const 
{
    std::vector<std::string> connected;
    for (size_t i = 0; i < m_readers.size(); ++i) 
    {
        if (m_readers[i]->IsInitialized() && m_readers[i]->IsDataFresh()) 
        {
            connected.push_back(m_characterNames[i]);
        }
    }
    return connected;
}

bool E3NextInventoryManager::HasFreshData(int maxAgeSeconds) const 
{
    for (const auto& reader : m_readers) 
    {
        if (reader->IsDataFresh(maxAgeSeconds)) 
        {
            return true;
        }
    }
    return false;
}

void E3NextInventoryManager::PrintStatus() const 
{
    WriteChatf("=== E3Next Inventory Integration Status ===");
    for (size_t i = 0; i < m_readers.size(); ++i) 
    {
        const std::string& name = m_characterNames[i];
        if (m_readers[i]->IsInitialized()) 
        {
            if (m_readers[i]->IsDataFresh()) 
            {
                WriteChatf("%s: Connected (Fresh Data)", name.c_str());
            }
            else 
            {
                WriteChatf("%s: Connected (Stale Data)", name.c_str());
            }
        }
        else 
        {
            WriteChatf("%s: Disconnected", name.c_str());
        }
    }
    WriteChatf("Cached inventories: %d", static_cast<int>(m_cachedInventories.size()));
    WriteChatf("==========================================");
}

bool E3NextInventoryManager::IsConnectedToCharacter(const std::string& characterName) const 
{
    int index = FindCharacterIndex(characterName);
    return index != -1 && m_readers[index]->IsInitialized();
}

void E3NextInventoryManager::ParseAndCacheInventoryData(const std::string& characterName, const std::string& jsonData)
{
    try
    {
        // Only parse if the JSON data has actually changed
        static std::unordered_map<std::string, std::string> s_lastJsonData;
        auto it = s_lastJsonData.find(characterName);
        if (it != s_lastJsonData.end() && it->second == jsonData) {
            // Data hasn't changed, skip parsing
            return;
        }
        s_lastJsonData[characterName] = jsonData;

        auto j = SimpleJSON::Parse(jsonData);

        if (!j.IsObject())
        {
            WriteChatf("[MQ2EZInv] Invalid JSON format from E3Next for character %s", characterName.c_str());
            return;
        }

        WriteChatf("[MQ2EZInv] Parsing inventory for %s", characterName.c_str());
        // The JSON contains a single character's inventory
        auto charObj = j.AsObject();
        
        InventoryData inventory;
        if (charObj.find("character") != charObj.end() && charObj.at("character").IsString())
        {
            inventory.characterName = charObj.at("character").AsString();
        }
        else
        {
            // Fallback to using the key as character name
            inventory.characterName = characterName;
        }

        if (charObj.find("class") != charObj.end() && charObj.at("class").IsString())
        {
            inventory.characterClass = charObj.at("class").AsString();
        }

        if (charObj.find("server") != charObj.end() && charObj.at("server").IsString())
        {
            std::string rawServerName = charObj.at("server").AsString();
            inventory.serverName = rawServerName;
            WriteChatf("[MQ2EZInv] Server name from JSON for %s: '%s' (raw: '%s')", characterName.c_str(), inventory.serverName.c_str(), rawServerName.c_str());
        }
        else
        {
            // Use current character's server as fallback
            inventory.serverName = EZInvUtils::GetServerName();
            WriteChatf("[MQ2EZInv] Using fallback server name for %s: %s", characterName.c_str(), inventory.serverName.c_str());
        }

        // Note: MQ2EZInv uses chrono::time_point, not string for timestamp
        inventory.lastUpdate = std::chrono::system_clock::now();

        // Parse equipped items
        if (charObj.find("equipped") != charObj.end() && charObj.at("equipped").IsArray())
        {
            auto equippedArray = charObj.at("equipped").AsArray();
            for (const auto& item : equippedArray)
            {
                if (!item.IsObject()) continue;
                auto itemObj = item.AsObject();

                ItemData itemData;
                if (itemObj.find("id") != itemObj.end() && itemObj.at("id").IsNumber())
                    itemData.id = itemObj.at("id").AsInt();
                if (itemObj.find("name") != itemObj.end() && itemObj.at("name").IsString())
                    itemData.name = itemObj.at("name").AsString();
                if (itemObj.find("stack") != itemObj.end() && itemObj.at("stack").IsNumber())
                    itemData.qty = itemObj.at("stack").AsInt();
                if (itemObj.find("slotId") != itemObj.end() && itemObj.at("slotId").IsNumber())
                    itemData.slotid = itemObj.at("slotId").AsInt();
                if (itemObj.find("bagId") != itemObj.end() && itemObj.at("bagId").IsNumber())
                    itemData.bagid = itemObj.at("bagId").AsInt();
                if (itemObj.find("icon") != itemObj.end() && itemObj.at("icon").IsNumber())
                    itemData.icon = itemObj.at("icon").AsInt();
                if (itemObj.find("noDrop") != itemObj.end() && itemObj.at("noDrop").IsBool())
                    itemData.nodrop = itemObj.at("noDrop").AsBool() ? 0 : 1;
                if (itemObj.find("itemLink") != itemObj.end() && itemObj.at("itemLink").IsString())
                    itemData.itemlink = itemObj.at("itemLink").AsString();

                // Parse basic stats
                if (itemObj.find("ac") != itemObj.end() && itemObj.at("ac").IsNumber())
                    itemData.ac = itemObj.at("ac").AsInt();
                if (itemObj.find("hp") != itemObj.end() && itemObj.at("hp").IsNumber())
                    itemData.hp = itemObj.at("hp").AsInt();
                if (itemObj.find("mana") != itemObj.end() && itemObj.at("mana").IsNumber())
                    itemData.mana = itemObj.at("mana").AsInt();
                if (itemObj.find("endurance") != itemObj.end() && itemObj.at("endurance").IsNumber())
                    itemData.endurance = itemObj.at("endurance").AsInt();
                if (itemObj.find("itemtype") != itemObj.end() && itemObj.at("itemtype").IsString())
                    itemData.itemtype = itemObj.at("itemtype").AsString();
                if (itemObj.find("value") != itemObj.end() && itemObj.at("value").IsNumber())
                    itemData.value = itemObj.at("value").AsInt();
                if (itemObj.find("tribute") != itemObj.end() && itemObj.at("tribute").IsNumber())
                    itemData.tribute = itemObj.at("tribute").AsInt();

                // Parse augments
                if (itemObj.find("aug1Name") != itemObj.end() && itemObj.at("aug1Name").IsString())
                    itemData.aug1Name = itemObj.at("aug1Name").AsString();
                if (itemObj.find("aug1Link") != itemObj.end() && itemObj.at("aug1Link").IsString())
                    itemData.aug1Link = itemObj.at("aug1Link").AsString();
                if (itemObj.find("aug1Icon") != itemObj.end() && itemObj.at("aug1Icon").IsNumber())
                    itemData.aug1Icon = itemObj.at("aug1Icon").AsInt();

                if (itemObj.find("aug2Name") != itemObj.end() && itemObj.at("aug2Name").IsString())
                    itemData.aug2Name = itemObj.at("aug2Name").AsString();
                if (itemObj.find("aug2Link") != itemObj.end() && itemObj.at("aug2Link").IsString())
                    itemData.aug2Link = itemObj.at("aug2Link").AsString();
                if (itemObj.find("aug2Icon") != itemObj.end() && itemObj.at("aug2Icon").IsNumber())
                    itemData.aug2Icon = itemObj.at("aug2Icon").AsInt();

                if (itemObj.find("aug3Name") != itemObj.end() && itemObj.at("aug3Name").IsString())
                    itemData.aug3Name = itemObj.at("aug3Name").AsString();
                if (itemObj.find("aug3Link") != itemObj.end() && itemObj.at("aug3Link").IsString())
                    itemData.aug3Link = itemObj.at("aug3Link").AsString();
                if (itemObj.find("aug3Icon") != itemObj.end() && itemObj.at("aug3Icon").IsNumber())
                    itemData.aug3Icon = itemObj.at("aug3Icon").AsInt();

                if (itemObj.find("aug4Name") != itemObj.end() && itemObj.at("aug4Name").IsString())
                    itemData.aug4Name = itemObj.at("aug4Name").AsString();
                if (itemObj.find("aug4Link") != itemObj.end() && itemObj.at("aug4Link").IsString())
                    itemData.aug4Link = itemObj.at("aug4Link").AsString();
                if (itemObj.find("aug4Icon") != itemObj.end() && itemObj.at("aug4Icon").IsNumber())
                    itemData.aug4Icon = itemObj.at("aug4Icon").AsInt();

                if (itemObj.find("aug5Name") != itemObj.end() && itemObj.at("aug5Name").IsString())
                    itemData.aug5Name = itemObj.at("aug5Name").AsString();
                if (itemObj.find("aug5Link") != itemObj.end() && itemObj.at("aug5Link").IsString())
                    itemData.aug5Link = itemObj.at("aug5Link").AsString();
                if (itemObj.find("aug5Icon") != itemObj.end() && itemObj.at("aug5Icon").IsNumber())
                    itemData.aug5Icon = itemObj.at("aug5Icon").AsInt();

                if (itemObj.find("aug6Name") != itemObj.end() && itemObj.at("aug6Name").IsString())
                    itemData.aug6Name = itemObj.at("aug6Name").AsString();
                if (itemObj.find("aug6Link") != itemObj.end() && itemObj.at("aug6Link").IsString())
                    itemData.aug6Link = itemObj.at("aug6Link").AsString();
                if (itemObj.find("aug6Icon") != itemObj.end() && itemObj.at("aug6Icon").IsNumber())
                    itemData.aug6Icon = itemObj.at("aug6Icon").AsInt();

                inventory.equipped.push_back(itemData);
            }
        }

        // Parse bag items - MQ2EZInv stores bags differently
        if (charObj.find("bags") != charObj.end() && charObj.at("bags").IsArray())
        {
            auto bagsArray = charObj.at("bags").AsArray();
            for (const auto& item : bagsArray)
            {
                if (!item.IsObject()) continue;
                auto itemObj = item.AsObject();

                ItemData itemData;
                if (itemObj.find("id") != itemObj.end() && itemObj.at("id").IsNumber())
                    itemData.id = itemObj.at("id").AsInt();
                if (itemObj.find("name") != itemObj.end() && itemObj.at("name").IsString())
                    itemData.name = itemObj.at("name").AsString();
                if (itemObj.find("stack") != itemObj.end() && itemObj.at("stack").IsNumber())
                    itemData.qty = itemObj.at("stack").AsInt();
                if (itemObj.find("slotId") != itemObj.end() && itemObj.at("slotId").IsNumber())
                    itemData.slotid = itemObj.at("slotId").AsInt();
                if (itemObj.find("bagId") != itemObj.end() && itemObj.at("bagId").IsNumber())
                    itemData.bagid = itemObj.at("bagId").AsInt();
                if (itemObj.find("icon") != itemObj.end() && itemObj.at("icon").IsNumber())
                    itemData.icon = itemObj.at("icon").AsInt();
                if (itemObj.find("noDrop") != itemObj.end() && itemObj.at("noDrop").IsBool())
                    itemData.nodrop = itemObj.at("noDrop").AsBool() ? 0 : 1;
                if (itemObj.find("itemLink") != itemObj.end() && itemObj.at("itemLink").IsString())
                    itemData.itemlink = itemObj.at("itemLink").AsString();

                // Parse basic stats
                if (itemObj.find("ac") != itemObj.end() && itemObj.at("ac").IsNumber())
                    itemData.ac = itemObj.at("ac").AsInt();
                if (itemObj.find("hp") != itemObj.end() && itemObj.at("hp").IsNumber())
                    itemData.hp = itemObj.at("hp").AsInt();
                if (itemObj.find("mana") != itemObj.end() && itemObj.at("mana").IsNumber())
                    itemData.mana = itemObj.at("mana").AsInt();
                if (itemObj.find("endurance") != itemObj.end() && itemObj.at("endurance").IsNumber())
                    itemData.endurance = itemObj.at("endurance").AsInt();
                if (itemObj.find("itemtype") != itemObj.end() && itemObj.at("itemtype").IsString())
                    itemData.itemtype = itemObj.at("itemtype").AsString();
                if (itemObj.find("value") != itemObj.end() && itemObj.at("value").IsNumber())
                    itemData.value = itemObj.at("value").AsInt();
                if (itemObj.find("tribute") != itemObj.end() && itemObj.at("tribute").IsNumber())
                    itemData.tribute = itemObj.at("tribute").AsInt();

                // Parse augments
                if (itemObj.find("aug1Name") != itemObj.end() && itemObj.at("aug1Name").IsString())
                    itemData.aug1Name = itemObj.at("aug1Name").AsString();
                if (itemObj.find("aug1Link") != itemObj.end() && itemObj.at("aug1Link").IsString())
                    itemData.aug1Link = itemObj.at("aug1Link").AsString();
                if (itemObj.find("aug1Icon") != itemObj.end() && itemObj.at("aug1Icon").IsNumber())
                    itemData.aug1Icon = itemObj.at("aug1Icon").AsInt();

                if (itemObj.find("aug2Name") != itemObj.end() && itemObj.at("aug2Name").IsString())
                    itemData.aug2Name = itemObj.at("aug2Name").AsString();
                if (itemObj.find("aug2Link") != itemObj.end() && itemObj.at("aug2Link").IsString())
                    itemData.aug2Link = itemObj.at("aug2Link").AsString();
                if (itemObj.find("aug2Icon") != itemObj.end() && itemObj.at("aug2Icon").IsNumber())
                    itemData.aug2Icon = itemObj.at("aug2Icon").AsInt();

                if (itemObj.find("aug3Name") != itemObj.end() && itemObj.at("aug3Name").IsString())
                    itemData.aug3Name = itemObj.at("aug3Name").AsString();
                if (itemObj.find("aug3Link") != itemObj.end() && itemObj.at("aug3Link").IsString())
                    itemData.aug3Link = itemObj.at("aug3Link").AsString();
                if (itemObj.find("aug3Icon") != itemObj.end() && itemObj.at("aug3Icon").IsNumber())
                    itemData.aug3Icon = itemObj.at("aug3Icon").AsInt();

                if (itemObj.find("aug4Name") != itemObj.end() && itemObj.at("aug4Name").IsString())
                    itemData.aug4Name = itemObj.at("aug4Name").AsString();
                if (itemObj.find("aug4Link") != itemObj.end() && itemObj.at("aug4Link").IsString())
                    itemData.aug4Link = itemObj.at("aug4Link").AsString();
                if (itemObj.find("aug4Icon") != itemObj.end() && itemObj.at("aug4Icon").IsNumber())
                    itemData.aug4Icon = itemObj.at("aug4Icon").AsInt();

                if (itemObj.find("aug5Name") != itemObj.end() && itemObj.at("aug5Name").IsString())
                    itemData.aug5Name = itemObj.at("aug5Name").AsString();
                if (itemObj.find("aug5Link") != itemObj.end() && itemObj.at("aug5Link").IsString())
                    itemData.aug5Link = itemObj.at("aug5Link").AsString();
                if (itemObj.find("aug5Icon") != itemObj.end() && itemObj.at("aug5Icon").IsNumber())
                    itemData.aug5Icon = itemObj.at("aug5Icon").AsInt();

                if (itemObj.find("aug6Name") != itemObj.end() && itemObj.at("aug6Name").IsString())
                    itemData.aug6Name = itemObj.at("aug6Name").AsString();
                if (itemObj.find("aug6Link") != itemObj.end() && itemObj.at("aug6Link").IsString())
                    itemData.aug6Link = itemObj.at("aug6Link").AsString();
                if (itemObj.find("aug6Icon") != itemObj.end() && itemObj.at("aug6Icon").IsNumber())
                    itemData.aug6Icon = itemObj.at("aug6Icon").AsInt();

                // MQ2EZInv stores bags as unordered_map<int, vector<ItemData>>
                inventory.bags[itemData.bagid].push_back(itemData);
            }
        }

        // Parse bank items
        if (charObj.find("bank") != charObj.end() && charObj.at("bank").IsArray())
        {
            auto bankArray = charObj.at("bank").AsArray();
            for (const auto& item : bankArray)
            {
                if (!item.IsObject()) continue;
                auto itemObj = item.AsObject();

                ItemData itemData;
                if (itemObj.find("id") != itemObj.end() && itemObj.at("id").IsNumber())
                    itemData.id = itemObj.at("id").AsInt();
                if (itemObj.find("name") != itemObj.end() && itemObj.at("name").IsString())
                    itemData.name = itemObj.at("name").AsString();
                if (itemObj.find("stack") != itemObj.end() && itemObj.at("stack").IsNumber())
                    itemData.qty = itemObj.at("stack").AsInt();
                if (itemObj.find("slotId") != itemObj.end() && itemObj.at("slotId").IsNumber())
                {
                    itemData.slotid = itemObj.at("slotId").AsInt();
                }
                if (itemObj.find("bankSlotId") != itemObj.end() && itemObj.at("bankSlotId").IsNumber())
                {
                    itemData.bankslotid = itemObj.at("bankSlotId").AsInt();
                }
                else if (itemData.slotid >= 0)
                {
                    // If bankSlotId is not provided, use slotid as bankslotid for bank items
                    itemData.bankslotid = itemData.slotid;
                }
                if (itemObj.find("bagId") != itemObj.end() && itemObj.at("bagId").IsNumber())
                    itemData.bagid = itemObj.at("bagId").AsInt();
                if (itemObj.find("icon") != itemObj.end() && itemObj.at("icon").IsNumber())
                    itemData.icon = itemObj.at("icon").AsInt();
                if (itemObj.find("noDrop") != itemObj.end() && itemObj.at("noDrop").IsBool())
                    itemData.nodrop = itemObj.at("noDrop").AsBool() ? 0 : 1;
                if (itemObj.find("itemLink") != itemObj.end() && itemObj.at("itemLink").IsString())
                    itemData.itemlink = itemObj.at("itemLink").AsString();

                // Parse basic stats
                if (itemObj.find("ac") != itemObj.end() && itemObj.at("ac").IsNumber())
                    itemData.ac = itemObj.at("ac").AsInt();
                if (itemObj.find("hp") != itemObj.end() && itemObj.at("hp").IsNumber())
                    itemData.hp = itemObj.at("hp").AsInt();
                if (itemObj.find("mana") != itemObj.end() && itemObj.at("mana").IsNumber())
                    itemData.mana = itemObj.at("mana").AsInt();
                if (itemObj.find("endurance") != itemObj.end() && itemObj.at("endurance").IsNumber())
                    itemData.endurance = itemObj.at("endurance").AsInt();
                if (itemObj.find("itemtype") != itemObj.end() && itemObj.at("itemtype").IsString())
                    itemData.itemtype = itemObj.at("itemtype").AsString();
                if (itemObj.find("value") != itemObj.end() && itemObj.at("value").IsNumber())
                    itemData.value = itemObj.at("value").AsInt();
                if (itemObj.find("tribute") != itemObj.end() && itemObj.at("tribute").IsNumber())
                    itemData.tribute = itemObj.at("tribute").AsInt();

                // Parse augments
                if (itemObj.find("aug1Name") != itemObj.end() && itemObj.at("aug1Name").IsString())
                    itemData.aug1Name = itemObj.at("aug1Name").AsString();
                if (itemObj.find("aug1Link") != itemObj.end() && itemObj.at("aug1Link").IsString())
                    itemData.aug1Link = itemObj.at("aug1Link").AsString();
                if (itemObj.find("aug1Icon") != itemObj.end() && itemObj.at("aug1Icon").IsNumber())
                    itemData.aug1Icon = itemObj.at("aug1Icon").AsInt();

                if (itemObj.find("aug2Name") != itemObj.end() && itemObj.at("aug2Name").IsString())
                    itemData.aug2Name = itemObj.at("aug2Name").AsString();
                if (itemObj.find("aug2Link") != itemObj.end() && itemObj.at("aug2Link").IsString())
                    itemData.aug2Link = itemObj.at("aug2Link").AsString();
                if (itemObj.find("aug2Icon") != itemObj.end() && itemObj.at("aug2Icon").IsNumber())
                    itemData.aug2Icon = itemObj.at("aug2Icon").AsInt();

                if (itemObj.find("aug3Name") != itemObj.end() && itemObj.at("aug3Name").IsString())
                    itemData.aug3Name = itemObj.at("aug3Name").AsString();
                if (itemObj.find("aug3Link") != itemObj.end() && itemObj.at("aug3Link").IsString())
                    itemData.aug3Link = itemObj.at("aug3Link").AsString();
                if (itemObj.find("aug3Icon") != itemObj.end() && itemObj.at("aug3Icon").IsNumber())
                    itemData.aug3Icon = itemObj.at("aug3Icon").AsInt();

                if (itemObj.find("aug4Name") != itemObj.end() && itemObj.at("aug4Name").IsString())
                    itemData.aug4Name = itemObj.at("aug4Name").AsString();
                if (itemObj.find("aug4Link") != itemObj.end() && itemObj.at("aug4Link").IsString())
                    itemData.aug4Link = itemObj.at("aug4Link").AsString();
                if (itemObj.find("aug4Icon") != itemObj.end() && itemObj.at("aug4Icon").IsNumber())
                    itemData.aug4Icon = itemObj.at("aug4Icon").AsInt();

                if (itemObj.find("aug5Name") != itemObj.end() && itemObj.at("aug5Name").IsString())
                    itemData.aug5Name = itemObj.at("aug5Name").AsString();
                if (itemObj.find("aug5Link") != itemObj.end() && itemObj.at("aug5Link").IsString())
                    itemData.aug5Link = itemObj.at("aug5Link").AsString();
                if (itemObj.find("aug5Icon") != itemObj.end() && itemObj.at("aug5Icon").IsNumber())
                    itemData.aug5Icon = itemObj.at("aug5Icon").AsInt();

                if (itemObj.find("aug6Name") != itemObj.end() && itemObj.at("aug6Name").IsString())
                    itemData.aug6Name = itemObj.at("aug6Name").AsString();
                if (itemObj.find("aug6Link") != itemObj.end() && itemObj.at("aug6Link").IsString())
                    itemData.aug6Link = itemObj.at("aug6Link").AsString();
                if (itemObj.find("aug6Icon") != itemObj.end() && itemObj.at("aug6Icon").IsNumber())
                    itemData.aug6Icon = itemObj.at("aug6Icon").AsInt();

                inventory.bank.push_back(itemData);
            }
        }

        // Cache the inventory only if it has changed
        auto cachedIt = m_cachedInventories.find(inventory.characterName);
        if (cachedIt == m_cachedInventories.end() || cachedIt->second.Serialize() != inventory.Serialize()) {
            m_cachedInventories[inventory.characterName] = inventory;
            WriteChatf("[MQ2EZInv] Successfully parsed and cached inventory for %s", inventory.characterName.c_str());
        }
    }
    catch (const std::exception& e)
    {
        WriteChatf("[MQ2EZInv] Error parsing E3Next inventory data for character %s: %s", characterName.c_str(), e.what());
    }
}

int E3NextInventoryManager::FindCharacterIndex(const std::string& characterName) const 
{
    for (size_t i = 0; i < m_characterNames.size(); ++i) 
    {
        if (m_characterNames[i] == characterName) 
        {
            return static_cast<int>(i);
        }
    }
    return -1;
}

// E3Integration namespace implementation
namespace E3Integration 
{
    bool Initialize() 
    {
        g_e3InventoryManager = std::make_unique<E3NextInventoryManager>();
        return g_e3InventoryManager->Initialize();
    }
    
    void Shutdown() 
    {
        if (g_e3InventoryManager) 
        {
            g_e3InventoryManager->Shutdown();
            g_e3InventoryManager.reset();
        }
    }
    
    void Update() 
    {
        if (g_e3InventoryManager) 
        {
            g_e3InventoryManager->Update();
        }
    }
    
    bool GetCharacterInventory(const std::string& characterName, InventoryData& inventory) 
    {
        
        // Try regular E3 integration first
        if (g_e3InventoryManager && g_e3InventoryManager->GetCharacterInventory(characterName, inventory)) 
        {
            WriteChatf("[MQ2EZInv] Found inventory from regular E3 integration for: %s", characterName.c_str());
            return true;
        }
        
        // Try Binary E3 integration
        if (BinaryE3Integration::GetCachedInventory(characterName, inventory)) 
        {
            return true;
        }
        
        // If we discovered the character but don't have inventory data yet, 
        // create a placeholder inventory so the UI can show the character
        auto discoveredChars = BinaryE3Integration::DiscoverE3NextCharacters();
        
        if (std::find(discoveredChars.begin(), discoveredChars.end(), characterName) != discoveredChars.end()) {
            inventory.characterName = characterName;
            
            // Use consistent server name format
            inventory.serverName = EZInvUtils::GetServerName();
            
            inventory.lastUpdate = std::chrono::system_clock::now();
            // Leave equipped, bags, and bank empty for now
            return true;
        }
        
        WriteChatf("[MQ2EZInv] Character %s not found in any integration", characterName.c_str());
        return false;
    }
    
    std::vector<std::string> GetConnectedCharacters() 
    {
        std::vector<std::string> allCharacters;
        
        // Get characters from regular E3 integration
        if (g_e3InventoryManager) 
        {
            auto e3chars = g_e3InventoryManager->GetConnectedCharacters();
            allCharacters.insert(allCharacters.end(), e3chars.begin(), e3chars.end());
        }
        
        // Get characters from Binary E3 integration
        auto binaryChars = BinaryE3Integration::DiscoverE3NextCharacters();
        allCharacters.insert(allCharacters.end(), binaryChars.begin(), binaryChars.end());
        
        // Remove duplicates
        std::sort(allCharacters.begin(), allCharacters.end());
        allCharacters.erase(std::unique(allCharacters.begin(), allCharacters.end()), allCharacters.end());
        
        return allCharacters;
    }
    
    bool IsE3NextAvailable() 
    {
        // Check regular E3 integration
        if (g_e3InventoryManager && g_e3InventoryManager->HasFreshData()) 
        {
            return true;
        }
        
        // Check Binary E3 integration
        if (BinaryE3Integration::IsE3NextAvailable()) 
        {
            return true;
        }
        
        // Also consider it available if we have discovered characters, even without cached data
        auto discoveredChars = BinaryE3Integration::DiscoverE3NextCharacters();
        if (!discoveredChars.empty()) {
            return true;
        }
        
        return false;
    }
    
    std::vector<std::string> DiscoverE3NextCharacters() 
    {
        std::vector<std::string> candidates;
        
        // Method 1: Scan for existing E3Next shared memory spaces
        // E3Next creates shared memory with format: "E3_EZInv_{CharacterName}"
        
        // Method 2: Group/raid discovery disabled due to API compatibility issues
        // Shared memory discovery handles this better anyway
        
        // Method 3: Try to enumerate shared memory objects (Windows specific)
        // Using a more comprehensive approach with a wider range of character names
        
        // Generate a comprehensive list of potential character names to test
        std::vector<std::string> testNames;
        
        // Add common character name patterns - customize these with your actual naming patterns
        std::vector<std::string> baseNames = {
            "Warrior", "Cleric", "Paladin", "Ranger", "Shadowknight", "Druid",
            "Monk", "Bard", "Rogue", "Shaman", "Necromancer", "Wizard", 
            "Magician", "Enchanter", "Beastlord", "Berserker",
            "Degoju", "Donomoan", "Dureln", "Ebhove", "Estos", "Fateve", 
            "Fehaver", "Gifiren", "Gobedogu", "Hehici", "Kelythar", "Lerdari",
            "Linaheal", "Okhealz", "Pacoha", "Ubjuu", "Udmame", "Vepaon",
            "Wedyin", "Woroon", "Xutafu", "Zefios", "Zudau"
        };
        
        // Add base names
        for (const auto& baseName : baseNames) {
            testNames.push_back(baseName);
        }
        
        // Add numbered variants (1-100 to be more comprehensive)
        for (const auto& baseName : baseNames) {
            for (int i = 1; i <= 100; i++) {
                testNames.push_back(baseName + std::to_string(i));
            }
        }
        
        // Add common prefixes and suffixes
        std::vector<std::string> prefixes = {"", "My", "Bot", "Alt", "Bank", "Mule", "Char"};
        std::vector<std::string> suffixes = {"", "1", "2", "3", "Bot", "Alt", "Bank", "Mule", "Char"};
        
        for (const auto& prefix : prefixes) {
            for (const auto& baseName : baseNames) {
                for (const auto& suffix : suffixes) {
                    if (prefix.empty() && suffix.empty()) continue; // Skip base names already tested
                    std::string testName = prefix + baseName + suffix;
                    testNames.push_back(testName);
                }
            }
        }
        
        // Test each potential name by trying to open their shared memory
        int foundCount = 0;
        for (const auto& testName : testNames) {
            std::string memoryName = "E3_EZInv_" + testName;
            HANDLE hMapFile = OpenFileMapping(FILE_MAP_READ, FALSE, memoryName.c_str());
            if (hMapFile != NULL) {
                candidates.push_back(testName);
                CloseHandle(hMapFile);
                foundCount++;
            }
        }
        
        if (foundCount > 0) {
            // WriteChatf("[MQ2EZInv] Scan found %d characters", foundCount);
        }
        
        // Remove duplicates
        std::sort(candidates.begin(), candidates.end());
        candidates.erase(std::unique(candidates.begin(), candidates.end()), candidates.end());
        
        // WriteChatf("[MQ2EZInv] E3Next character discovery found %d potential characters", static_cast<int>(candidates.size()));
        
        return candidates;
    }
}
