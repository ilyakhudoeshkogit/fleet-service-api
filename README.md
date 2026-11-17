# Fleet Service API

REST API на C# / ASP.NET Core 8 для заявок на обслуживание коммерческого транспорта:
машины, заявки на ремонт, статусы и фоновая проверка просроченных заявок.

## Что умеет
- **Машины**: добавить, получить список, удалить (вместе с заявками). Номер приводится к верхнему регистру, дубли не пускаются (409).
- **Заявки**: создать, получить список с фильтрами `status`, `overdue`, `vehicleId`; сортировка — сначала срочные, потом по сроку.
- **Срок по приоритету**: если срок не указан, он ставится сам: High — 4 часа, Normal — 1 день, Low — 3 дня.
- **Статусы**: только допустимые переходы New → InProgress → Done, отмена из New/InProgress; остальное — 409.
- **Фоновая задача** (`BackgroundService` + `PeriodicTimer`): раз в N секунд помечает открытые заявки с прошедшим сроком как просроченные.
- **Статистика**: `/api/stats` — количество заявок по статусам и просроченных.
- Валидация входных данных (ValidationProblem), Swagger UI, async/await везде.

## Стек
C# 12, .NET 8, ASP.NET Core Minimal API, Entity Framework Core 8 (SQLite), xUnit + WebApplicationFactory, Swagger.
Для PostgreSQL достаточно поменять провайдер (`UseNpgsql`) и строку подключения, модель и запросы не меняются.

## Запуск
```bash
dotnet run --project src/FleetService.Api
# Swagger: http://localhost:5242/swagger
```
Через Docker:
```bash
docker build -t fleet-service-api .
docker run -p 8080:8080 fleet-service-api
```

Пример запросов:
```bash
curl -X POST localhost:5242/api/vehicles -H "content-type: application/json" \
  -d '{"plateNumber":"1234 ab-7","model":"MAN TGS","mileageKm":120000}'
curl -X POST localhost:5242/api/requests -H "content-type: application/json" \
  -d '{"vehicleId":1,"description":"Не работает ABS","priority":"High"}'
curl "localhost:5242/api/requests?overdue=true"
```

## Тесты
```bash
dotnet test
```
8 интеграционных тестов: валидация, дубли, срок по приоритету, переходы статусов, фоновая проверка просрочки
(с подменой времени через `TimeProvider`), сортировка, каскадное удаление. База в тестах — SQLite в памяти.

## Структура
```
src/FleetService.Api
  Models/      сущности и DTO
  Data/        DbContext и настройка модели
  Services/    правила сроков и статусов, фоновая проверка
  Endpoints/   REST-эндпоинты
tests/FleetService.Tests   интеграционные тесты
```

## Лицензия
MIT, см. [LICENSE](LICENSE).
