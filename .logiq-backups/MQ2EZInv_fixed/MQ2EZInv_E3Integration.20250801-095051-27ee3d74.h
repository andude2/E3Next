#pragma once

// Integration layer to use E3Next's shared memory instead of MQ2EZInv's own IPC system
// This replaces the MQ2EZInv_IPC.h system with E3Next shared memory

#include "SharedMemoryReader.h"
#include <vector>
#include <unordered_map>
#include <memory>
#include <chrono>
#include <string>

// Forward declarations
struct InventoryData;
struct ItemData;

// Forward declaration for direct networking
class E3NextDirectNetworking;

class E3NextInventoryManager 
{
private:
    std::unique_ptr<E3NextDirectNetworking> m_directNetworking;
private:
    std::vector<std::unique_ptr<SharedMemoryReader>> m_readers;
    std::vector<std::string> m_characterNames;
    std::unordered_map<std::string, InventoryData> m_cachedInventories;
    std::chrono::steady_clock::time_point m_lastUpdate;
    static constexpr auto UPDATE_INTERVAL = std::chrono::seconds(2); // Changed from 1 to 2 seconds to reduce stuttering // Changed from 1 to 2 seconds

public:
    E3NextInventoryManager();
    ~E3NextInventoryManager();

    // Initialize connections to E3Next characters
    bool Initialize();
    void Shutdown();

    // Add/remove characters to monitor
    bool AddCharacter(const std::string& characterName);
    void RemoveCharacter(const std::string& characterName);
    
    // Update inventory data from E3Next shared memory
    void Update();

    // Get inventory data (replaces the old MQ2EZInv IPC system)
    const std::unordered_map<std::string, InventoryData>& GetAllInventories() const { return m_cachedInventories; }
    bool GetCharacterInventory(const std::string& characterName, InventoryData& inventory) const;
    
    // Get connected characters (replaces peer discovery)
    std::vector<std::string> GetConnectedCharacters() const;
    
    // Check if data is fresh
    bool HasFreshData(int maxAgeSeconds = 60) const;
    
    // Status information
    void PrintStatus() const;
    bool IsConnectedToCharacter(const std::string& characterName) const;

private:
    void ParseAndCacheInventoryData(const std::string& characterName, const std::string& jsonData);
    int FindCharacterIndex(const std::string& characterName) const;
};

// Global instance to replace the old IPC system
extern std::unique_ptr<E3NextInventoryManager> g_e3InventoryManager;

// Compatibility functions to replace MQ2EZInv IPC calls
namespace E3Integration 
{
    // Initialize E3Next integration (call this instead of old IPC init)
    bool Initialize();
    void Shutdown();
    
    // Update data (call this in OnPulse instead of old IPC update)
    void Update();
    
    // Get inventory data (replaces old SharedInventoryData access)
    bool GetCharacterInventory(const std::string& characterName, InventoryData& inventory);
    std::vector<std::string> GetConnectedCharacters();
    
    // Check if E3Next is running and providing data
    bool IsE3NextAvailable();
    
    // Auto-discover E3Next characters (reads from E3Next bot list if available)
    std::vector<std::string> DiscoverE3NextCharacters();
}