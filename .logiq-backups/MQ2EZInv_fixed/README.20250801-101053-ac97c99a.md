# MQ2EZInv

MQ2EZInv is a MacroQuest 2 plugin for EverQuest that provides advanced inventory management and peer-to-peer inventory sharing. It now integrates seamlessly with E3Next to provide real-time inventory data across multiple characters.

## Features

- **Inventory Management**: Advanced inventory UI with sorting, filtering, and search capabilities
- **Peer-to-Peer Sharing**: Share inventory data with other characters using E3Next's shared memory system
- **Direct Network Publishing**: Real-time inventory publishing via named pipes to E3Next's NetMQ system
- **Bank Integration**: Full support for bank slots and shared bank
- **Item Suggestions**: Smart item suggestions based on your characters' needs
- **Cross-character Trading**: Initiate trades between characters directly

## Getting Started

### Prerequisites

- MacroQuest 2 installed and running
- E3Next installed and configured
- InventoryActorManager enabled in E3Next

### Installation

1. Copy MQ2EZInv.dll to your MacroQuest plugins folder
2. Start EverQuest and load MQ2EZInv
3. Verify E3Next integration with `/ezinve3 status`

### Basic Usage

```txt
/plugin MQ2EZInv
/ezinv          # Open/close inventory UI
/ezinve3 status  # Check E3Next integration status
/ezinve3 force   # Force discovery of E3Next characters
```

## Getting Started

Quick start instructions to get users up and going

```txt
/plugin MQ2EZInv
```

## Commands

### Main Commands
```txt
/ezinv          # Toggle inventory UI
/ezinv scan     # Force inventory refresh
/ezinv sync     # Force sync with all peers
/ezinv peers    # Show connected characters
```

### E3Next Integration Commands
```txt
/ezinve3 status          # Show E3Next integration status
/ezinve3 scan           # Scan for E3Next characters
/ezinve3 force          # Force-add all known E3Next characters
/ezinve3 connect <char> # Connect to specific character
/ezinve3 add <char1,char2> # Add multiple characters
```

### Configuration Commands
```txt
/ezinvstats [minimal|selective|full] # Configure stats loading
/ezinvconfig [save|reload|sync]      # Configuration management
```

### Item Management
```txt
/ezinv bank <item>      # Toggle bank flag for item
/ezinv bank list        # List bank flagged items
/ezinv bank auto        # Auto-bank flagged items
```

## Configuration

MQ2EZInv uses E3Next's configuration system. No separate configuration file is required.

### Performance Settings

- **Stats Loading**: Configure level of item detail loaded
  - `minimal`: Only essential item data
  - `selective`: Basic stats (AC, HP, Mana)
  - `full`: All item statistics (default)

## E3Next Integration

MQ2EZInv integrates with E3Next to provide real-time inventory sharing:

- **Shared Memory**: Reads from `E3_EZInv_{CharacterName}` 
- **Real-time Updates**: Gets inventory data as E3Next processes it
- **Auto-discovery**: Automatically finds other E3Next characters
- **Fallback Mode**: Works locally if E3Next is unavailable

## Troubleshooting

### Common Issues

1. **E3Next not detected**: 
   - Ensure E3Next is running
   - Verify InventoryActorManager is enabled
   - Run `/ezinve3 force` to force character discovery

2. **No inventory data**:
   - Check `/ezinve3 status` for connection details
   - Verify characters are running E3Next
   - Check E3Next logs for errors

3. **Performance issues**:
   - Try `/ezinvstats minimal` for reduced overhead
   - Close unused inventory windows
   - Check for large item databases

## Authors

* **Original MQ2EZInv Development Team**
* **E3Next Integration**: Enhanced to work with E3Next's shared memory system

## Contributing

This project is part of the MQ2EZInv ecosystem. Contributions are welcome through the standard MacroQuest 2 contribution process.

## Acknowledgments

* Original MQ2EZInv authors for the foundation
* E3Next team for the excellent framework
* MacroQuest 2 community for testing and feedback
