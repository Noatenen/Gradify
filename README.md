# 🎓 Gradify

Gradify is a web-based system for managing academic final projects.  
The platform provides a centralized environment for students, mentors, and lecturers to track progress, manage tasks, and improve communication throughout the project lifecycle.

---

## 🚀 Features

### 👩‍🎓 Students
- View project status and progress
- Manage milestones and submissions
- Track upcoming deadlines
- Submit requests (extensions, support, etc.)

### 👨‍🏫 Lecturers / Admins
- Manage users (students, mentors)
- Manage teams and project assignments
- Monitor project health and delays
- Handle special requests

### 👨‍💼 Mentors
- Track assigned projects
- Monitor student progress
- Review submissions

---

## 🏗️ Tech Stack

- **Frontend**: Blazor WebAssembly
- **Backend**: ASP.NET Core Web API
- **Database**: SQLite
- **Authentication**:
  - JWT
  - Google OAuth
- **Architecture**:
  - Client / Server / Shared (Clean separation)

---

## 📂 Project Structure


Gradify/
│
├── Client/ # Blazor WebAssembly frontend
├── Server/ # ASP.NET Core backend (API, Auth, DB)
├── Shared/ # Shared models and DTOs


---

## 🔐 Security Notes

Sensitive data such as:
- JWT keys
- API keys
- Email credentials

are NOT included in this repository.

Use local configuration files like:

appsettings.Development.json


---

## ⚙️ Getting Started

### 1. Clone the repository
```bash
git clone https://github.com/Noatenen/Gradify.git
2. Configure environment

Create:

Server/appsettings.Development.json

and fill in your local secrets.

3. Run the project
cd Server
dotnet run
📌 Future Improvements
Role-based authorization (fine-grained permissions)
Real-time notifications
Integration with external systems (Moodle / Airtable)
Advanced analytics dashboard
👩‍💻 Authors
Noa Tenenbaum
Ofir Sharabi
📄 License

This project is part of an academic final project and is intended for educational use.
