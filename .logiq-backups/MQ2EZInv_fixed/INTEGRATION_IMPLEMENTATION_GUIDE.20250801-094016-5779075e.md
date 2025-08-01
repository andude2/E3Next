# Integration Implementation Guide

## Overview

This guide explains how to implement the direct network integration between MQ2EZInv_fixed and E3Next's networking layer.

## C# Implementation (E3Next Side)

### 1. InventoryDataSharing.cs

The core component handles inventory data sharing through E3Next's NetMQ system.

```csharp
// Key methods:
public static void PublishInventoryData(string jsonInventoryData)
{
    if (!string.IsNullOrEmpty(jsonInventoryData))
    {
        PubServer.AddTopicMessage(INVENTORY_DATA_TOPIC, jsonInventoryData);
    }
}

public static void ProcessInventoryData(string senderName, string inventoryData)
{
    if (!string.IsNullOrEmpty(senderName) && !string.IsNullOrEmpty(inventoryData))
    {
        lock (_receivedInventoryData)
        {
            _receivedInventoryData[senderName] = inventoryData;
            _lastDataUpdate[senderName] = DateTime.UtcNow;
        }
    }
}
```

### 2. Integration with PubServer

Extended PubServer with inventory-specific methods:

```csharp
public static void PublishInventoryData(string inventoryData)
{
    if (!string.IsNullOrEmpty(inventoryData))
    {
        AddTopicMessage("InventoryData", inventoryData);
    }
}

public static void RequestInventoryData()
{
    AddTopicMessage("RequestInventory", "Request");
}
```

### 3. Integration with SharedDataClient

Extended SharedDataClient to handle inventory data:

```csharp
public void ProcessReceivedInventoryData(string senderName, string inventoryData)
{
    // Process and cache received inventory data
    ConcurrentDictionary<string, ShareDataEntry> usertopics;
    if (!TopicUpdates.TryGetValue(senderName, out usertopics))
    {
        usertopics = new ConcurrentDictionary<string, ShareDataEntry>();
        TopicUpdates.TryAdd(senderName, usertopics);
    }

    ShareDataEntry entry;
    if (!usertopics.TryGetValue("InventoryData", out entry))
    {
        entry = new ShareDataEntry() { Data = inventoryData, LastUpdate = Core.StopWatch.ElapsedMilliseconds };
        usertopics.TryAdd("InventoryData", entry);
    }
    else
    {
        lock (entry)
        {
            entry.Data = inventoryData;
            entry.LastUpdate = Core.StopWatch.ElapsedMilliseconds;
        }
    }
}
```

## C++ Implementation (MQ2EZInv_fixed Side)

### 1. MQ2EZInv_E3Integration.h

Core C++ class for E3Next integration:

```cpp
class E3NextInventoryManager 
{
private:
    std::vector<std::unique_ptr<SharedMemoryReader>> m_readers;
    std::vector<std::string> m_characterNames;
    std::unordered_map<std::string, InventoryData> m_cachedInventories;
    std::chrono::steady_clock::time_point m_lastUpdate;
    
public:
    bool Initialize();
    void Shutdown();
    bool AddCharacter(const std::string& characterName);
    void RemoveCharacter(const std::string& characterName);
    void Update();
    const std::unordered_map<std::string, InventoryData>& GetAllInventories() const;
    bool GetCharacterInventory(const std::string& characterName, InventoryData& inventory) const;
    std::vector<std::string> GetConnectedCharacters() const;
    bool HasFreshData(int maxAgeSeconds = 60) const;
    void PrintStatus() const;
};
```

### 2. Integration with Plugin Core

The main plugin file (MQ2EZInv.cpp) is updated to include the E3Next integration:

```cpp
// Global instance
std::unique_ptr<E3NextInventoryManager> g_e3InventoryManager;

// In InitializePlugin():
if (!E3Integration::Initialize()) {
    WriteChatf("[MQ2EZInv] Warning: E3Next integration failed - falling back to local-only mode");
}

// In OnPulse():
E3Integration::Update();
```

## Data Flow

### Publishing Inventory Data

1. MQ2EZInv_fixed serializes inventory data to JSON
2. Data is sent directly through E3Next's NetMQ Publisher
3. Other clients receive the data through their Subscriber sockets
4. Received data is cached and made available to the plugin

### Receiving Inventory Data

1. E3Next's NetMQ Subscriber receives inventory data
2. Data is processed by SharedDataClient and cached
3. MQ2EZInv_fixed accesses cached data through integration layer
4. Data is deserialized and made available to UI and other systems

## Network Topics

The system uses specific NetMQ topics for inventory data:

- `InventoryData` - For publishing/receiving inventory data
- `RequestInventory` - For requesting inventory data from other clients

## Error Handling

The system includes robust error handling:

- Network disconnection detection and reconnection
- Data validation and integrity checking
- Cache expiration and cleanup
- Graceful degradation when networking is unavailable

## Performance Considerations

- Data is published at intervals to prevent network flooding
- Cache expiration prevents stale data usage
- Minimal memory footprint with efficient data structures
- Non-blocking operations to prevent UI freezing

## Security

The system operates within the trusted E3Next network:

- Only authenticated E3Next clients can participate
- Data is transmitted within the local network or VPN
- No external internet exposure

## Testing

To test the integration:

1. Start multiple E3Next clients
2. Verify inventory data is shared between clients
3. Check that data updates are received in real-time
4. Confirm proper error handling with network disruptions

## Troubleshooting

Common issues and solutions:

1. **No data received**: Check NetMQ port connectivity
2. **Stale data**: Verify cache expiration settings
3. **Network errors**: Check firewall and network configuration
4. **Performance issues**: Review publish intervals and data size

## Conclusion

This integration provides a robust, efficient way to share inventory data directly through E3Next's networking infrastructure, offering significant improvements over file-based communication.