# MQ2EZInv E3Next Integration Guide

This guide explains how MQ2EZInv uses E3Next's shared memory system for inventory sharing.

## Current Implementation

MQ2EZInv has been migrated to use E3Next's shared memory system instead of its own separate IPC system:
- **Names**: `E3_EZInv_{CharacterName}` and `E3_EZInv_{CharacterName}_Mutex`
- **Location**: E3Next's shared memory spaces
- **Data**: JSON format with all character inventories
- **Advantage**: Real-time updates from E3Next's NetMQ pub/sub system

## Usage

### 1. Initialization

MQ2EZInv automatically initializes E3Next integration:

```cpp
// In plugin initialization (already handled)
if (!E3Integration::Initialize()) {
    WriteChatf("[MQ2EZInv] Warning: E3Next integration failed, running without shared data");
}
```

### 2. Update Loop

MQ2EZInv automatically handles updates in OnPulse:

```cpp
// In OnPulse (already handled)
PLUGIN_API void OnPulse() {
    // Update E3Next integration
    E3Integration::Update();
    
    // Other plugin code...
}
```

### 3. Data Access

Access inventory data through E3Next integration:

```cpp
// Use E3Next integration
InventoryData inventory;
if (E3Integration::GetCharacterInventory("CharacterName", inventory)) {
    // Use inventory data directly
    for (const auto& item : inventory.equipped) {
        // Process equipped items
    }
    for (const auto& item : inventory.bags) {
        // Process bag items  
    }
    for (const auto& item : inventory.bank) {
        // Process bank items
    }
}
```

### 4. Character Discovery

Use E3Next's character discovery:

```cpp
// Get connected characters
std::vector<std::string> connectedChars = E3Integration::GetConnectedCharacters();
```

### 5. Shutdown

Cleanup is handled automatically:

```cpp
// In plugin shutdown (already handled)
void ShutdownPlugin() {
    E3Integration::Shutdown();
    // Other cleanup...
}
```

## Verification Steps

### 1. Confirm E3Next Connection
Add this command to verify the integration is working:

```cpp
// Add this command to your plugin
void ShowE3Status() {
    if (g_e3InventoryManager) {
        g_e3InventoryManager->PrintStatus();
    } else {
        WriteChatf("[MQ2EZInv] E3Next integration not initialized");
    }
}
```

### 2. Manual Character Connection
If auto-discovery doesn't work, manually add characters:

```cpp
// Add this command to manually connect to E3Next characters
void ConnectToE3Character(const std::string& characterName) {
    if (g_e3InventoryManager && g_e3InventoryManager->AddCharacter(characterName)) {
        WriteChatf("[MQ2EZInv] Connected to E3Next character: %s", characterName.c_str());
    } else {
        WriteChatf("[MQ2EZInv] Failed to connect to E3Next character: %s", characterName.c_str());
    }
}
```

### 3. Test Data Flow
Verify data is coming from E3Next, not MQ2EZInv's own system:

```cpp
void TestE3Data() {
    auto connectedChars = E3Integration::GetConnectedCharacters();
    WriteChatf("[MQ2EZInv] Connected to %d E3Next characters:", static_cast<int>(connectedChars.size()));
    
    for (const auto& charName : connectedChars) {
        InventoryData inventory;
        if (E3Integration::GetCharacterInventory(charName, inventory)) {
            WriteChatf("  %s: %d equipped, %d bags, %d bank items", 
                      charName.c_str(),
                      static_cast<int>(inventory.equipped.size()),
                      static_cast<int>(inventory.bags.size()),
                      static_cast<int>(inventory.bank.size()));
        }
    }
}
```

## Benefits After Migration

1. **No Duplicate Systems**: MQ2EZInv uses E3Next's data instead of maintaining its own
2. **Real-time Updates**: Gets inventory updates as soon as E3Next processes them  
3. **Centralized Data**: All inventory data comes from E3Next's authoritative source
4. **Reduced Memory**: No duplicate inventory storage
5. **Simplified Maintenance**: No need to maintain separate IPC system

## Rollback Plan

If issues occur, you can temporarily re-enable the old system by:
1. Commenting out E3Integration calls
2. Re-enabling the old ActorNetworkUtils calls  
3. Using preprocessor directives to switch between systems

```cpp
#define USE_E3_INTEGRATION 1

#if USE_E3_INTEGRATION
    E3Integration::Update();
#else
    ActorNetworkUtils::UpdateHeartbeat();
    ActorNetworkUtils::FetchCommands();
#endif
```