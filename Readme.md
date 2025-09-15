# MarketBot - Market Ecosystem Simulator

MarketBot is a sophisticated **Market Ecosystem Simulator** designed to create artificial market activity in myDU servers. It simulates realistic market conditions by generating buy/sell orders, managing virtual inventory, and maintaining economic activity across multiple planets and item tiers (T0-T5).

**🚀 Key Capabilities:**
- Multi-planet market management with tier-based processing
- Virtual inventory system decoupled from game inventory  
- Advanced pricing algorithms with market analysis and caching
- Service-oriented architecture with dependency injection
- Tick-based concurrent processing for scalability
- Production-ready Docker deployment

## Table of Contents

- [Features](#features)
- [Architecture](#architecture)
- [How It Works](#how-it-works)
- [Configuration](#configuration)
- [Installation & Usage](#installation--usage)
- [Services Overview](#services-overview)
- [Performance & Scaling](#performance--scaling)
- [Contributing](#contributing)
- [License](#license)

## Features

### Core Market Simulation
- **🌍 Multi-Planet Operations:** Manages market activity across multiple planets simultaneously
- **📊 Tier-Based Processing:** Handles items in tiers T0-T5 with configurable quantity thresholds
- **🔄 Dynamic Order Generation:** Creates realistic buy/sell orders based on market conditions
- **💰 Advanced Pricing:** Recursive recipe cost calculation with market analysis and outlier filtering
- **📦 Virtual Inventory Management:** Redis-backed inventory system separate from game inventory
- **⚡ Concurrent Processing:** Multi-threaded tick-based processing with CPU-core scaling

### Advanced Features
- **🔧 Sophisticated Configuration:** Tier-specific settings for Resources, Components, and Products
- **📈 Market Analytics:** Real-time market data analysis with caching and statistics
- **🛡️ Robust Error Handling:** Automatic bot reconnection and retry mechanisms
- **📊 Monitoring Integration:** Prometheus metrics and operational dashboards
- **🐳 Docker Ready:** Production deployment with container orchestration
- **🔄 Service Architecture:** Modular, testable, and maintainable service design

## Implemented Features (Previously "Future")
- ✅ **Advanced pricing system** with tier-based and crafting-time calculations
- ✅ **Buy/sell order creation** for all item types including resources
- ✅ **Complex crafting system** with component dependency handling
- ✅ **Sophisticated resource management** with market-driven purchasing decisions
- ✅ **Real-time market monitoring** with dynamic pricing adjustments

## Architecture

MarketBot uses a sophisticated **service-oriented architecture** with clear separation of concerns:

```
ModMarketBot (Entry Point)
├── MarketOverlord (Multi-planet orchestration)
│   ├── MarketManager (Per-planet operations) × N planets
│   │   ├── MarketService (Market API interactions)
│   │   ├── PriceService (Pricing calculations)
│   │   ├── InventoryService (Virtual inventory)
│   │   ├── RecipeService (Item categorization)
│   │   └── CraftingQueue (Crafting operations)
│   └── StatisticsScheduler (Monitoring)
├── TickService (Concurrent processing)
├── ServiceCollectionExtensions (DI container)
└── Complex Configuration (Tier-based settings)
```

### Key Architectural Components
- **MarketOverlord:** Orchestrates market operations across multiple planets
- **MarketManager:** Handles market operations for individual planets/markets
- **TickService:** Manages concurrent processing with semaphore-controlled threading
- **PriceService:** Advanced pricing with recursive recipe cost calculation
- **InventoryService:** Virtual inventory tracking separate from game inventory
- **MarketService:** All market API interactions with retry mechanisms
- **RecipeService:** Game data management and item categorization

## How It Works

### Market Simulation Process

1. **Tick-Based Processing:**
   - System processes markets in **1-second ticks** with concurrent execution
   - Each MarketManager handles one planet with multiple markets
   - Processes **5 items per tick** to prevent system overload
   - Cycles through tiers T0-T5 systematically

2. **Market Analysis & Decision Making:**
   - Analyzes current buy/sell orders for each item
   - Compares against configured tier-based thresholds
   - Calculates optimal pricing using recursive recipe costs
   - Determines whether to create buy orders, sell orders, or craft items

3. **Order Generation:**
   - **Buy Orders:** Created when market has insufficient buy orders for an item
   - **Sell Orders:** Created when virtual inventory exceeds configured thresholds
   - **Pricing:** Based on market analysis and recipe cost calculations
   - **Quantities:** Determined by tier-specific configuration settings

4. **Crafting Operations:**
   - Items are crafted when market conditions favor it over buying
   - Crafting decisions based on cost analysis vs. market prices
   - Respects recipe dependencies and crafting times
   - Adds crafted items to virtual inventory for later selling

5. **Virtual Inventory Management:**
   - Maintains separate inventory from game bot inventory
   - Tracks items per market for accurate market-specific operations
   - Automatically moves items between bot inventory and virtual inventory
   - Uses Redis for persistence across restarts

6. **Market Ecosystem Simulation:**
   - Creates realistic market activity across all configured planets
   - Maintains economic balance through tier-based quantity management
   - Responds dynamically to market conditions and price fluctuations
   - Provides liquidity for both common and rare items

## Configuration

MarketBot uses a sophisticated **tier-based configuration system** that allows fine-grained control over market behavior across different item tiers (T0-T5) and types (Resource/Component/Product).

### Main Configuration Structure

```json
{
    "Market": {
        "MarketOperationsTickInSeconds": 60,
        "QueueProcessingTickInSeconds": 5,
        "OperationMarkets": [3, 4, 29]
    },
    "MarketOverlord": {
        "TickInSeconds": 60,
        "OperationPlanets": [2],
        "OreBaselinePrices": {
            "1": 25,
            "2": 35,
            "3": 45,
            "4": 55,
            "5": 65
        },
        "TierSettings": {
            "0": {
                "Resource": {
                    "NumberOfBuyOrders": 5,
                    "NumberOfSellOrders": 1,
                    "QuantityInInventory": 10000,
                    "QuantityInSellOrders": 1000,
                    "QuantityInBuyOrders": 1000
                },
                "Component": { /* ... */ },
                "Product": { /* ... */ }
            },
            "1": { /* T1 settings */ },
            "2": { /* T2 settings */ },
            /* ... T3-T5 settings */
        }
    }
}
```

### Configuration Options

#### Market Settings
- **MarketOperationsTickInSeconds:** Legacy setting (kept for compatibility)
- **QueueProcessingTickInSeconds:** Crafting queue processing interval
- **OperationMarkets:** List of market IDs for operations

#### MarketOverlord Settings
- **TickInSeconds:** Main processing tick interval (1 second recommended)
- **OperationPlanets:** List of planet IDs to manage markets on
- **OreBaselinePrices:** Baseline prices for resource tiers T1-T5

#### Tier Settings Structure
For each tier (T0-T5), configure behavior for each item type:

- **NumberOfBuyOrders:** Target number of buy orders to maintain
- **NumberOfSellOrders:** Target number of sell orders to maintain
- **QuantityInInventory:** Virtual inventory threshold
- **QuantityInSellOrders:** Total quantity across all sell orders
- **QuantityInBuyOrders:** Total quantity across all buy orders

### Configuration Examples

#### High-Volume T0/T1 Resources:
```json
"Resource": {
    "NumberOfBuyOrders": 5,
    "NumberOfSellOrders": 5,
    "QuantityInInventory": 10000,
    "QuantityInSellOrders": 1000,
    "QuantityInBuyOrders": 5000
}
```

#### Low-Volume T5 Products:
```json
"Product": {
    "NumberOfBuyOrders": 1,
    "NumberOfSellOrders": 1,
    "QuantityInInventory": 0,
    "QuantityInSellOrders": 2,
    "QuantityInBuyOrders": 1
}
```

### Advanced Configuration

- **Recipe Management:** Edit `Data/recipes.json` to remove unwanted recipes
- **Market Selection:** Configure specific markets in `Data/markets.json`
- **Baseline Pricing:** Adjust `OreBaselinePrices` for different economic conditions
- **Performance Tuning:** Adjust tick intervals and batch sizes for your server

## Installation & Usage

### Prerequisites
- myDU server environment with Orleans clustering
- Redis instance for persistence (database 5)
- Docker for containerized deployment
- Bot credentials for myDU server

### Quick Start

1. **Clone and Configure:**
   ```bash
   git clone <repository-url>
   cd MarketBot
   ```

2. **Configure the System:**
   - Edit `config.json` with your desired tier settings
   - Update `OperationPlanets` to target your planets
   - Adjust `OreBaselinePrices` for your economy
   - Configure Redis connection in your myDU environment

3. **Build and Deploy:**
   ```bash
   # Build Docker image
   docker build -f Dockerfile -t marketbot .
   ```

4. **Docker Compose (recommended):**
   A ready-to-use compose file is provided as `docker-compose.yml`. It will:
   - Build from the local `Dockerfile`
   - Use a `.env` file for credentials
   - Mount `config.json` and your `dual.yaml` as read-only
   - Join the existing `mydu` network

   Example `.env`:
   ```env
   BOT_LOGIN=trader
   BOT_PASSWORD=secret
   # Optional if needed by your environment
   # QUEUEING=http://queueing:9630
   ```

   Compose file excerpt:
   ```yaml
   version: "3.8"
   services:
     marketbot:
       build:
         context: .
         dockerfile: Dockerfile
       container_name: marketbot
       restart: unless-stopped
       env_file:
         - .env
       volumes:
         - ./config.json:/Mod/config.json:ro
         - D:/DualUniverseServer/config/dual.yaml:/config/dual.yaml:ro
       networks:
         - mydu
   networks:
     mydu:
       external: true
   ```

### Development Setup

```powershell
# Build the project
dotnet build

# Run locally with configuration
$env:BOT_LOGIN="trader"
$env:BOT_PASSWORD="secret"
# $env:QUEUEING="http://queueing:9630"  # Optional if your environment needs it
dotnet run -- /config/dual.yaml /Mod/config.json
```

### Monitoring and Operations

- **Logs:** Check container logs for market operations and error handling
- **Metrics:** Prometheus metrics available for monitoring
- **Health Checks:** Monitor service startup and tick processing
- **Performance:** Watch CPU usage and memory consumption during peak operations

## Services Overview

### Core Services

| Service | Purpose | Key Features |
|---------|---------|-------------|
| **MarketOverlord** | Multi-planet orchestration | Planet management, service coordination |
| **MarketManager** | Per-planet operations | Tier processing, market simulation |
| **TickService** | Concurrent processing | Multi-threading, semaphore control |
| **MarketService** | Game API interactions | Order management, retry logic |
| **PriceService** | Pricing calculations | Recursive costing, market analysis |
| **InventoryService** | Virtual inventory | Redis persistence, market tracking |
| **RecipeService** | Item categorization | Tier classification, recipe management |
| **CraftingQueueService** | Crafting operations | Background processing, timing |
| **StatisticsScheduler** | Monitoring | Metrics collection, performance tracking |

### Service Interactions

- **Dependency Injection:** All services registered via `ServiceCollectionExtensions`
- **Error Handling:** Comprehensive retry mechanisms with `BotConnectionManager`
- **Persistence:** Redis-backed state management for inventory and statistics
- **Logging:** Structured logging throughout all services
- **Configuration:** Strongly-typed configuration binding

## Performance & Scaling

### Performance Characteristics

- **Tick Processing:** 1-second intervals with 5 items per tick
- **Concurrency:** CPU-core-based semaphore limiting
- **Caching:** 30-minute price cache with intelligent invalidation
- **Batch Processing:** Prevents API overloading with controlled batching
- **Memory Usage:** Efficient with lazy loading and caching strategies

### Scaling Considerations

- **Multi-Planet:** Horizontal scaling across planets
- **Tier Distribution:** Natural load balancing via tier-based processing
- **Redis Persistence:** Shared state enables multiple instances
- **API Rate Limiting:** Built-in protection against game API throttling
- **Resource Management:** Configurable limits prevent resource exhaustion

### Performance Tuning

```json
{
  "MarketOverlord": {
    "TickInSeconds": 1,           // Processing frequency
    "OperationPlanets": [2, 3, 4] // Scale across multiple planets
  }
}
```

- **Tick Interval:** Lower values increase responsiveness but use more resources
- **Items Per Tick:** Balance between throughput and API load
- **Planet Distribution:** Spread load across multiple planets for better performance

## Current Limitations

- **Configuration Complexity:** Tier-based configuration requires careful tuning
- **Market Dependency:** Requires active myDU server and functional market APIs
- **Redis Requirement:** Depends on Redis for state persistence and coordination
- **Resource Consumption:** Can be resource-intensive during peak operations
- **Learning Curve:** Complex architecture requires understanding for modifications

## Contributing

Contributions are welcome! Whether it's reporting bugs, suggesting new features, or improving the codebase, your input is valuable.

### How to Contribute

1. **Fork the Repository:** Create your own fork of the project.
2. **Create a Branch:** Make a new branch for your feature or bugfix.
3. **Make Changes:** Implement your changes or additions.
4. **Submit a Pull Request:** Provide a clear description of your changes for review.

## Feedback

As this is the author's first project in C#, feedback is highly encouraged. Please feel free to:

- Suggest improvements or optimizations.
- Request new features or functionality.
- Report issues or bugs you encounter.

You can submit feedback by opening an issue or participating in discussions in the project's repository.

## License

This project is licensed under the [MIT License](LICENSE).
