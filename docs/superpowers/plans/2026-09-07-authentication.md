# Аутентификация — план реализации

**Goal:** В интерфейс входят по логину и паролю, API не отвечает никому, кроме своих, а
забытый пароль восстанавливается командой на сервере.

**Spec:** `docs/superpowers/specs/2026-09-07-authentication-design.md`

**Ветка:** `feature/auth`

## Global Constraints

- Проекты: `ServerMonitor.Domain`, `ServerMonitor.Infrastructure`, `ServerMonitor.Api`,
  `ServerMonitor.Web`, `ServerMonitor.Tests`.
- Веб-проект **не получает** доступа к базе: проверка пароля идёт через API.
- Пароли не попадают ни в логи, ни в аргументы команд, ни в сообщения об ошибках.
- Ответ на неверный вход не различает «нет такого пользователя» и «неверный пароль».
- Секреты (служебный ключ) — в User Secrets, в `appsettings.json` только пустой шаблон.
- После каждой задачи: `dotnet build` без предупреждений, `dotnet test` зелёный, отдельный
  коммит без трейлера соавторства.

---

### Task 1: Сущность User и хранение пароля

**Files:**
- Create: `ServerMonitor.Domain/Entities/User.cs`
- Modify: `ServerMonitor.Infrastructure/Data/AppDbContext.cs`
- Create: `ServerMonitor.Infrastructure/Auth/PasswordService.cs`
- Modify: `ServerMonitor.Infrastructure/ServerMonitor.Infrastructure.csproj`
- Create: миграция `AddUsers`
- Test: `ServerMonitor.Tests/Auth/PasswordServiceTests.cs`

**Interfaces:**
- Produces: `PasswordService.Hash(string)`, `PasswordService.Verify(string hash, string password)`
  → `PasswordVerification` (`Failed`, `Success`, `SuccessRehashNeeded`). Использует задача 2.

- [ ] **Step 1: Тесты (падающие)**

```csharp
[Fact]
public void Hash_ProducesDifferentValuesForTheSamePassword()
{
    // Соль случайна на каждый вызов: одинаковые пароли не должны давать одинаковые хеши,
    // иначе по базе видно, у кого пароли совпадают.
    Assert.NotEqual(service.Hash("correct horse"), service.Hash("correct horse"));
}

[Fact]
public void Verify_AcceptsTheRightPassword() { ... }

[Fact]
public void Verify_RejectsTheWrongPassword() { ... }

[Fact]
public void Verify_RejectsGarbageHashWithoutThrowing()
{
    // В базе может оказаться мусор — например, после ручной правки. Это отказ, а не падение.
    Assert.Equal(PasswordVerification.Failed, service.Verify("not-a-hash", "any"));
}
```

- [ ] **Step 2: Пакет и реализация**

```bash
dotnet add ServerMonitor.Infrastructure package Microsoft.Extensions.Identity.Core
```

`PasswordService` — тонкая обёртка над `PasswordHasher<User>`: скрывает тип-параметр и
переводит результат в собственное перечисление, чтобы домен не зависел от ASP.NET Identity.

- [ ] **Step 3: Сущность и модель**

`User` с полями `Id`, `Username`, `PasswordHash`, `CreatedAtUtc`, `LastLoginUtc`.
В `AppDbContext` — `DbSet<User>` и **уникальный индекс** по `Username`.

- [ ] **Step 4: Миграция**

```bash
dotnet ef migrations add AddUsers --project ServerMonitor.Infrastructure --startup-project ServerMonitor.Api
```

Проверить, что создаётся уникальный индекс, применить, убедиться что таблица пуста.

- [ ] **Step 5: Commit**

---

### Task 2: Служба входа и защита от перебора

**Files:**
- Create: `ServerMonitor.Infrastructure/Auth/LoginThrottle.cs`
- Create: `ServerMonitor.Infrastructure/Auth/UserService.cs`
- Test: `ServerMonitor.Tests/Auth/LoginThrottleTests.cs`

**Interfaces:**
- Produces: `UserService.VerifyAsync`, `CreateAsync`, `SetPasswordAsync`, `AnyUsersAsync`,
  `ListAsync`, `DeleteAsync`. Использует задача 3.
- Produces: `LoginThrottle.IsBlocked(string user)`, `RecordFailure(string user)`,
  `Reset(string user)`.

- [ ] **Step 1: Тесты троттлинга (падающие)**

Ключевые случаи: до порога не блокирует; на пороге блокирует; успешный вход сбрасывает счётчик;
блокировка истекает по времени. Время — параметром, как в `HeartbeatRule`, иначе тест
непроверяем.

- [ ] **Step 2: Реализация троттлинга**

Словарь в памяти: имя → (счётчик, время блокировки). Порог 5 попыток, блокировка 5 минут.

> Счётчик по **имени пользователя**, а не по IP: за одним IP может сидеть весь дом, и блокировка
> по адресу превратилась бы в отказ в обслуживании для соседей. Обратная сторона — атакующий
> может заблокировать вход владельцу, зная его логин; для домашней системы это приемлемо.

- [ ] **Step 3: UserService**

Проверка пароля, создание, смена пароля, список, удаление. При `SuccessRehashNeeded` —
перезаписать хеш новым (бесплатное обновление параметров).

**Удаление последней учётки запрещено** — иначе система запрёт сама себя.

- [ ] **Step 4: Commit**

---

### Task 3: Эндпоинты и служебный ключ

**Files:**
- Create: `ServerMonitor.Api/Dtos/{LoginRequest,LoginResponse,CreateUserRequest,UserDto,AuthStateDto}.cs`
- Create: `ServerMonitor.Api/Controllers/AuthController.cs`
- Create: `ServerMonitor.Api/Auth/ServiceKeyAttribute.cs`
- Modify: контроллеры `Servers`, `Metrics`, `Alerts`, `Settings` (навесить атрибут)
- Modify: `ServerMonitor.Api/appsettings.json` (`Api:ServiceKey`)

- [ ] **Step 1: Фильтр служебного ключа**

`ServiceKeyAttribute` — фильтр действия: сверяет `X-Service-Key` с настройкой за постоянное
время (`ApiKeyGenerator.FixedTimeEquals`). Если ключ **не настроен** — отказ, а не пропуск:
незаданный секрет не должен означать «пускать всех».

- [ ] **Step 2: AuthController**

- `GET api/auth/state` → `{ hasUsers }`
- `POST api/auth/setup` → создать первую учётку, **только если таблица пуста**
- `POST api/auth/login` → проверить пароль; 401 без подробностей
- `GET api/auth/users`, `POST api/auth/users`, `DELETE api/auth/users/{id}`

Все — под служебным ключом.

- [ ] **Step 3: Навесить защиту на остальные контроллеры**

`Servers`, `Metrics`, `Alerts`, `Settings` получают `[ServiceKey]`.
`Ingest` и `Agents` — **не трогать**: у них своя проверка.

- [ ] **Step 4: Проверить запросами**

```bash
curl -sk -o /dev/null -w "%{http_code}\n" https://localhost:7212/api/servers
```
Expected: `401`

```bash
curl -sk -o /dev/null -w "%{http_code}\n" -H "X-Service-Key: <ключ>" https://localhost:7212/api/servers
```
Expected: `200`

Приём метрик от агента продолжает работать.

- [ ] **Step 5: Commit**

---

### Task 4: Вход в веб-интерфейсе

**Files:**
- Modify: `ServerMonitor.Web/Program.cs` (cookie-схема, передача служебного ключа)
- Modify: `ServerMonitor.Web/Services/MetricsApiClient.cs` (заголовок ключа)
- Create: `ServerMonitor.Web/Services/AuthApiClient.cs`
- Create: `ServerMonitor.Web/Components/Pages/Login.razor` + `.razor.css`
- Create: `ServerMonitor.Web/Components/Pages/Setup.razor`
- Create: `ServerMonitor.Web/Endpoints/AccountEndpoints.cs`
- Modify: `ServerMonitor.Web/Components/Routes.razor` (`AuthorizeRouteView`)
- Modify: `ServerMonitor.Web/Components/Layout/TopNav.razor` (имя и выход)
- Modify: `ServerMonitor.Web/appsettings.json`

- [ ] **Step 1: Cookie-аутентификация**

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
```

Плюс `app.UseAuthentication()` и `app.UseAuthorization()` **до** `MapRazorComponents`.

- [ ] **Step 2: Страница входа без интерактива**

`Login.razor` с `@attribute [ExcludeFromInteractiveRouting]` и обычной HTML-формой, которая
шлёт POST на `/account/login`. Никакого `@onclick` — именно в этом весь смысл (см. спеку).

- [ ] **Step 3: Эндпоинты аккаунта**

`POST /account/login` — спросить API, при успехе `SignInAsync` и редирект; при неудаче — назад
на `/login?error=1`. `POST /account/logout` — `SignOutAsync` и редирект.

- [ ] **Step 4: Закрыть страницы**

`Routes.razor` → `AuthorizeRouteView` с `NotAuthorized`, ведущим на `/login`.
На каждую страницу — `@attribute [Authorize]`, кроме `Login` и `Setup`.

- [ ] **Step 5: Первичная настройка**

`Setup.razor` — та же схема без интерактива. Доступна, только пока `hasUsers == false`;
иначе редирект на `/login`.

- [ ] **Step 6: Имя пользователя и выход в шапке**

- [ ] **Step 7: Проверить в браузере**

Все семь пунктов проверки из спеки.

- [ ] **Step 8: Commit**

---

### Task 5: Команда сброса пароля

**Files:**
- Create: `ServerMonitor.Api/Commands/ResetPasswordCommand.cs`
- Modify: `ServerMonitor.Api/Program.cs`

- [ ] **Step 1: Разбор аргументов до старта веб-сервера**

```csharp
if (args is ["reset-password", var username])
{
    return await ResetPasswordCommand.RunAsync(builder.Configuration, username);
}
```

- [ ] **Step 2: Чтение пароля без эха**

`Console.ReadKey(intercept: true)` посимвольно, с поддержкой Backspace. Подтверждение вводом
второй раз. Пустой пароль и короче 8 символов — отказ.

- [ ] **Step 3: Проверить**

Сменить пароль, убедиться что старый не пускает, новый пускает, и что пароль не виден на экране
и не попал в историю оболочки.

- [ ] **Step 4: Commit**

---

### Task 6: Управление учётками в настройках

**Files:**
- Modify: `ServerMonitor.Web/Components/Pages/Settings.razor`
- Modify: `ServerMonitor.Web/Services/AuthApiClient.cs`

- [ ] **Step 1: Секция «Accounts»** — список, добавление, удаление с подтверждением.
- [ ] **Step 2: Себя удалить нельзя, последнюю учётку удалить нельзя.**
- [ ] **Step 3: Проверить в браузере**
- [ ] **Step 4: Commit**

---

### Task 7: Глава 12 гайда

**Files:**
- Create: `docs/guide/12-authentication.md`
- Modify: `docs/guide/README.md`, `README.md`

- [ ] **Step 1: Написать главу**

Разделы: три действующих лица и почему у них разные механизмы; почему cookie нельзя выставить
из интерактивного компонента (главная техническая мысль этапа); медленный хеш против быстрого —
возврат к главе 10; почему не пишем PBKDF2 руками; одинаковый ответ на «нет пользователя» и
«неверный пароль»; троттлинг по имени, а не по IP, и чем за это платим; почему незаданный
секрет означает «никого не пускать»; граница доступа при сбросе пароля.

- [ ] **Step 2: Обновить README** — убрать пункт про открытые эндпоинты.
- [ ] **Step 3: Commit**
