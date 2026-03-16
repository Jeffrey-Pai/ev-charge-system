# EV Charge System (Docker + API + EF + MSSQL + MQ)

這是一個可直接啟動的「充電樁後端系統」範例專案，包含：

- ASP.NET Core Web API
- Entity Framework Core（SQL Server）
- RabbitMQ（流程事件 MQ）
- Docker Compose（一鍵啟動 API + MSSQL + RabbitMQ）

## 1. 系統目標

提供基本充電樁核心流程：

- 使用者認證（API Key）
- 預約充電
- 啟動充電
- 結束充電
- 查詢充電狀態與充電樁狀態

並透過 RabbitMQ 將流程拆成「API 命令 + 背景工作流事件處理」，方便後續擴充成分散式架構。

## 2. 技術架構

- API: ASP.NET Core 8 Web API
- ORM: EF Core 8 + SQL Server
- MQ: RabbitMQ topic exchange
- Infra: Docker Compose

### 主要服務

- `api`: `http://localhost:8080`
- `sqlserver`: `localhost:11433`
- `rabbitmq`: `localhost:5673`（容器內仍是 `5672`）
- RabbitMQ 管理頁: `http://localhost:15673` (`guest/guest`)

## 3. 專案結構

```
ev-charge-system/
  EvChargeSystem.Api/
    Controllers/
      AuthController.cs
      ChargingController.cs
    Data/
      ChargingDbContext.cs
    Infrastructure/
      ApiKeyMiddleware.cs
    Messaging/
      RabbitMqEventBus.cs
      IEventBus.cs
      Events.cs
      RabbitMqOptions.cs
    Models/
      Entities/
      Dtos/
    Services/
      DatabaseInitializerHostedService.cs
      ChargingWorkflowConsumer.cs
    Dockerfile
  docker-compose.yml
  README.md
```

## 4. 啟動方式（Docker）

在 `ev-charge-system` 根目錄執行：

```bash
docker compose up --build -d
```

查看服務狀態：

```bash
docker compose ps
```

若服務沒有正常起來，先看狀態與 logs：

```bash
docker compose ps
docker compose logs api --tail 200
docker compose logs rabbitmq --tail 200
docker compose logs sqlserver --tail 200
```

開啟 Swagger：

- `http://localhost:8080/swagger`
- `http://localhost:8080/`（會自動導向 Swagger，方便 debug）

### 4.1 怎麼看 MSSQL 資料庫內的資料

本專案的 SQL Server 設定（來自 `docker-compose.yml`）：

- Host: `localhost`
- Port: `11433`
- Database: `EvChargeDb`
- User: `sa`
- Password: `Your_password123`
- Container: `evcharge-sqlserver`

先確認資料庫容器有啟動：

```bash
docker compose ps
```

在 PowerShell 直接查詢（不用進 `/bin/bash`）：

```bash
docker exec evcharge-sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "Your_password123" -d EvChargeDb -Q "SELECT TOP 10 * FROM Chargers"
```

如果你的容器沒有 `mssql-tools18`，改用：

```bash
docker exec evcharge-sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "Your_password123" -d EvChargeDb -Q "SELECT TOP 10 * FROM Chargers"
```

你也可以先連進 `sqlcmd` 再手動查詢：

```bash
docker exec -it evcharge-sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S 127.0.0.1,1433 -U sa -P "Your_password123" -d EvChargeDb
```

連進去後會看到 `1>` 提示符，輸入 SQL 後用 `GO` 執行，例如：

```sql
SELECT TOP 20 * FROM UserAccounts ORDER BY Id DESC;
GO
```

離開 `sqlcmd`：

```sql
QUIT
```

常用查詢範例：

```sql
-- 列出資料表
SELECT TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE';

-- 看充電樁
SELECT TOP 20 * FROM Chargers ORDER BY Id DESC;

-- 看使用者
SELECT TOP 20 * FROM UserAccounts ORDER BY Id DESC;

-- 看預約
SELECT TOP 20 * FROM ChargingReservations ORDER BY Id DESC;

-- 看充電會話
SELECT TOP 20 * FROM ChargingSessions ORDER BY Id DESC;
```

如果你想用 GUI，看資料會更直覺：

- SSMS 或 Azure Data Studio 連線到 `localhost,11433`
- 帳號 `sa` / 密碼 `Your_password123`
- 展開 `Databases > EvChargeDb > Tables` 後即可直接查資料

若 SSMS 登不進去，請檢查：

1. Server name 請填 `localhost,11433`（不要只填 `localhost`）。
2. Authentication 選 `SQL Server Authentication`，Login=`sa`。
3. 在 SSMS 連線視窗按 `Options >>`：
  - Encrypt 設為 `Optional`（或 `Mandatory` 也可）。
  - 勾選 `Trust server certificate`。
4. 先確認 Docker 映射正常：`docker ps` 要看到 `0.0.0.0:11433->1433/tcp`。
5. 若你改過 `docker-compose.yml` 的 SA 密碼，需刪除舊 volume 才會套用新密碼：
  `docker compose down -v` 後再 `docker compose up -d --build`。
6. 如果仍是 18456，先用上面的 `docker exec ... sqlcmd` 驗證 `sa` 密碼是否正確；若正確但 SSMS 仍失敗，請改測 `127.0.0.1,11433`（不要用 `localhost`）。

## 5. API 使用流程

### 5.1 先建立帳號 / 拿 API Key

`POST /api/auth/register`

```json
{
  "userName": "alice"
}
```

回傳包含 `apiKey`，後續都放在 Header:

`X-Api-Key: <your-api-key>`

### 5.2 驗證 API Key

`POST /api/auth/validate`

```json
{
  "apiKey": "ev-xxxxxxxx"
}
```

### 5.3 查詢充電樁

`GET /api/charging/chargers`

### 5.4 預約充電

`POST /api/charging/reservations`

```json
{
  "chargerCode": "CP-001",
  "startAtUtc": "2026-03-13T08:00:00Z",
  "endAtUtc": "2026-03-13T10:00:00Z"
}
```

### 5.5 啟動充電

`POST /api/charging/start`

```json
{
  "chargerCode": "CP-001",
  "meterStartKwh": 1024.5
}
```

### 5.6 結束充電

`POST /api/charging/stop`

```json
{
  "sessionId": 1,
  "meterEndKwh": 1036.1
}
```

### 5.7 查詢充電會話

`GET /api/charging/sessions/{sessionId}`

## 6. MQ 流程規劃（重點）

目前實作的事件 routing key：

- `charging.start.requested`
- `charging.started`
- `charging.stop.requested`
- `charging.stopped`
- `charging.reservation.created`
- `charging.reservation.confirmed`
- `charging.reservation.expire.requested`
- `charging.reservation.expired`
- `charging.reservation.in_use`
- `charging.reservation.completed`

### 建議的充電樁流程編排

1. API 收到 `start` 請求，寫 DB（session=`PendingStart`）後發出 `charging.start.requested`
2. Workflow Consumer 收到事件，更新 DB（session=`Active`, charger=`Charging`），再發出 `charging.started`
3. API 收到 `stop` 請求，寫 DB（session=`PendingStop`）後發出 `charging.stop.requested`
4. Workflow Consumer 收到事件，更新 DB（session=`Completed`, charger=`Available`），再發出 `charging.stopped`
5. API 建立預約後發出 `charging.reservation.created`，Workflow Consumer 轉成 `charging.reservation.confirmed`
6. 背景服務定期掃描過期預約，發出 `charging.reservation.expire.requested`，Workflow Consumer 將預約標記 `Expired` 並在可釋放時把充電樁狀態改回 `Available`

## 7. 可擴充的 MQ 事件建議

你可以再加上以下事件，形成更完整的營運流程：

- `charging.auth.validated`
- `charging.reservation.expired`
- `charging.payment.requested`
- `charging.payment.completed`
- `charging.alarm.raised`（過熱、急停、通訊中斷）
- `charging.heartbeat.missed`

## 8. Mermaid 流程圖

```mermaid
flowchart LR
  A[Client API Start] --> B[Write DB PendingStart]
  B --> C[Publish charging.start.requested]
  C --> D[Workflow Consumer]
  D --> E[Update DB Active/Charging]
  E --> F[Publish charging.started]

  G[Client API Stop] --> H[Write DB PendingStop]
  H --> I[Publish charging.stop.requested]
  I --> D
  D --> J[Update DB Completed/Available]
  J --> K[Publish charging.stopped]
```

## 9. 開發備註

- 目前使用 `EnsureCreated` 自動建表，適合 Demo/MVP。
- 正式環境建議改成 EF Migration。
- 目前認證採 API Key Middleware，正式環境可改 JWT + OAuth2。
- 密碼/API Key 等敏感資訊請改用 Secret Manager 或 Vault。

## 10. 快速測試（cURL 範例）

建立使用者：

```bash
curl -X POST http://localhost:8080/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"userName":"alice"}'
```

查充電樁：

```bash
curl -X GET http://localhost:8080/api/charging/chargers \
  -H "X-Api-Key: demo-api-key-12345"
```

---

如果你要，我可以下一步幫你補：

1. JWT 驗證與角色權限
2. EF Migration 與資料版本控管
3. 充電費率計算與帳務事件流
4. OCPP 對接模擬器
