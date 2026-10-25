# 💊 Pharmacy Inventory Audit & Management System

**A modern desktop application for pharmacy inventory management, stock auditing, inventory movement tracking, and user access control.**

Built with **C#, WPF, .NET 8, and Microsoft SQL Server**, this project aims to make pharmacy inventory operations more organized, traceable, and efficient.

<p align="center">
  <strong>Manage Stock · Audit Inventory · Track Movements · Control Access</strong>
</p>

---

## 📌 Table of Contents

- [Overview](#-overview)
- [Key Features](#-key-features)
- [Technology Stack](#-technology-stack)
- [Application Structure](#-application-structure)
- [Requirements](#-requirements)
- [Getting Started](#-getting-started)
- [Database Configuration](#-database-configuration)
- [Build and Run](#-build-and-run)
- [Security](#-security)
- [Project Goals](#-project-goals)
- [Author](#-author)

---

## 🚀 Overview

The **Pharmacy Inventory Audit & Management System** is a Windows desktop application designed to support pharmacy inventory operations through a structured interface and a SQL Server database.

The application brings together inventory-related workflows, stock movement records, audit reporting, Excel-based inventory loading, and user management in one application.

Its goal is to help pharmacy teams organize inventory data, review stock discrepancies, maintain clearer records of inventory activity, and manage access to application functions.

## ✨ Key Features

### 📦 Inventory Management

- Organize and manage pharmacy product records.
- Load inventory data from Excel files.
- Support inventory-related workflows through a dedicated desktop interface.

### 🔍 Inventory Auditing

- Support inventory audit workflows and audit-related database structures.
- Review inventory differences and audit information.
- Provide dedicated audit reporting functionality.

### 📊 Reports and Inventory Movements

- Review inventory movement records.
- Generate and export reports through the application's reporting services.
- Use dedicated views for inventory movement and audit-related operations.

### 👥 User Management

- Dedicated user management interface.
- Authentication services.
- Password hashing support.
- Permission-related database scripts for application functionality.

### 🎨 User Interface

- Windows Presentation Foundation (WPF) desktop interface.
- Blue, Dark, and Light themes.
- Reusable XAML styles and UI components.
- Shared notification components for application messages.

### 🗄️ Database Support

- Microsoft SQL Server integration.
- Dedicated database service layer.
- SQL scripts for inventory audit tables, user management tables, and report permission updates.

---

## 🛠️ Technology Stack

| Technology                | Purpose                           |
| ------------------------- | --------------------------------- |
| C#                        | Application logic                 |
| .NET 8                    | Application framework             |
| WPF                       | Windows desktop user interface    |
| XAML                      | UI layout, styles, and themes     |
| Microsoft SQL Server      | Data storage                      |
| ADO.NET / SQL data access | Database communication            |
| Git & GitHub              | Version control and collaboration |

---

## 📁 Application Structure

```text
Pharmacy-Inventory-Audit-Management-System/
│
├── Assets/                 # Application assets and icon
├── Converters/             # WPF value converters
├── Database/               # SQL database scripts
├── Models/                 # Application data models
├── Services/               # Business and infrastructure services
├── Sounds/                 # Application sound resources
├── Styles/                 # Shared WPF control styles
├── Themes/                 # Blue, Dark, and Light themes
├── ViewModels/              # View models and commands
│
├── App.xaml                 # Application resources and startup
├── MainWindow.xaml          # Main application window
├── LoginWindow.xaml         # Login interface
├── AuditReportWindow.xaml   # Audit reporting interface
├── ItemMovementView.xaml    # Inventory movement view
├── UserManagementView.xaml  # User management view
│
├── PharmacyInventorySystem.csproj
├── PharmacyInventorySystem.slnx
└── README.md
```

---

## 💻 Requirements

Before running the application, ensure you have:

- Windows 10 or Windows 11.
- .NET 8 SDK.
- Microsoft SQL Server or a compatible SQL Server instance.
- Visual Studio 2022 or a compatible IDE with the .NET desktop development workload.
- Git, if you want to clone the repository.

---

## ⚙️ Getting Started

### 1. Clone the Repository

```bash
git clone https://github.com/Eng-Ahmed-El3Almey/Pharmacy-Inventory-Audit-Management-System.git
```

### 2. Open the Project

Navigate to the project directory:

```bash
cd Pharmacy-Inventory-Audit-Management-System
```

Open `PharmacyInventorySystem.slnx` in a compatible version of Visual Studio, or open the project file directly:

```text
PharmacyInventorySystem.csproj
```

### 3. Configure the Database

Set up a SQL Server database
