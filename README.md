# AutomationBrigde

> 💼 **Commercial Project** — internal solution for automated conveyor and PTL systems.

## Overview

**AutomationBrigde** is a .NET Worker Service solution containing two independent background services: **PLCWorker** and **PTLWorker**.

- **PLCWorker** acts as a TCP/IP server for PLC controllers, handling frame processing (SCN, CMD, WDG) and integrating with a SQL database via Dapper to determine package routing.
- **PTLWorker** synchronizes with the PTL system over TCP/IP, sending and receiving messages to coordinate material handling.

Both services are fully asynchronous, DI-friendly, and designed for scalable, maintainable industrial automation environments.

## Table of Contents

- [Overview](#overview)
- [Features](#features)
- [Architecture](#architecture)
- [Technologies Used](#technologies-used)
- [License](#license)

## Features

- Independent PLCWorker and PTLWorker services
- TCP/IP server with multi-client support for PLC communication
- Frame processing for SCN, CMD, and WDG messages
- PTL synchronization service for package tracking and coordination
- Database access via Dapper for efficient routing queries
- Fully asynchronous, non-blocking Worker Service architecture
- Modular, maintainable, and extendable design for industrial automation
- Centralized logging with Serilog

## Technologies Used

- **Framework:** .NET 8 Worker Service
- **Languages:** C#
- **Database:** SQL Server, Dapper
- **Logging:** Serilog
- **Networking:** TCP/IP communication with PLC and PTL systems

## License

This project is proprietary and confidential. See the [LICENSE](LICENSE) file for more information.

---

© 2025 [calKU0](https://github.com/calKU0)
