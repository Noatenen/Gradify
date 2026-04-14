# Project: Gradify

## Overview
Gradify is a final project management system for students, lecturers, and mentors.

The system is built using:
- Blazor WebAssembly (Client)
- ASP.NET Core Web API (Server)
- SQLite (Database)

The system manages:
- Projects
- Milestones
- Tasks
- Users (students, lecturers, mentors)
- Progress tracking

---

## Architecture Guidelines

- Follow existing project structure and naming conventions.
- Do NOT introduce new architectural patterns unless necessary.
- Reuse existing services, DTOs, and controllers whenever possible.
- Avoid code duplication.

---

## Coding Standards

- Use clear, readable, production-level code.
- Keep logic separated:
  - Controllers → thin
  - Services → business logic
  - DTOs → data transfer only
- Use async/await where appropriate.
- Validate inputs properly.

---

## Database Rules

- Use existing entities: Milestones, Tasks, Users.
- Prefer adding new tables instead of modifying core ones unless necessary.
- Use foreign keys properly.
- Do NOT store file binaries in DB unless already used in project.

---

## UI/UX Guidelines

- Maintain consistency with existing design:
  - colors
  - spacing
  - components
- Do NOT redesign the system.
- Follow the current dashboard/card style.
- Keep UI clean and minimal.

---

## File Upload Feature (Important Context)

We already implemented file upload in a previous assignment.

When working on file upload:
- Reuse the existing upload logic
- Do NOT create a completely new mechanism
- Follow the same pattern:
  - upload file
  - store file in server folder
  - save metadata in DB

---

## Feature: File Management (Admin/Lecturer)

We are building a feature where:
- Lecturers can upload files
- Each file MUST be مرتبط with a Milestone
- Each file CAN optionally be linked to a Task

This will later become a knowledge management system.

---

## Expectations from Claude

When implementing features:
1. First analyze the existing codebase
2. Reuse existing patterns
3. Propose minimal changes
4. Then implement

Always:
- Explain your decisions briefly
- List changed files
- Avoid breaking existing functionality

---

## Important Notes

- Do NOT over-engineer
- Keep things simple and scalable
- Prefer incremental improvements over big rewrites