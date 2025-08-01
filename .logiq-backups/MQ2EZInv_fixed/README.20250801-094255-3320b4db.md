# MQ2EZInv

MQ2EZInv is a MacroQuest 2 plugin for EverQuest that provides advanced inventory management and peer-to-peer inventory sharing. It now integrates seamlessly with E3Next to provide real-time inventory data across multiple characters.

## Features

- **Inventory Management**: Advanced inventory UI with sorting, filtering, and search capabilities
- **Peer-to-Peer Sharing**: Share inventory data with other characters using E3Next's shared memory system
- **Real-time Updates**: Receive inventory updates as soon as they happen
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

### Commands

Describe the commands available and how to use them.

```txt
Give examples
```

### Configuration File

Describe the configuration file and what the settings do

```yaml
- Example goes here
```

## Other Notes

Add additional notes

## Authors

* **Your name** - *Initial work*

See also the list of [contributors](https://github.com/your/project/contributors) who participated in this project.

## Acknowledgments

* Inspiration from...
* I'd like to thank the Thieves' Guild for helping me with all the code I stole...
