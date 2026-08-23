# 设计方案

## 1. 项目概述

业务流程：

1. 新用户注册账号，或已有用户登录系统，获取 Access Token 与 Refresh Token。
2. 用户请求获取一张待标注图片。
3. 服务端随机分配一张未完成图片。
4. 使用 Garnet 临时记录图片领取状态，防止多人同时标注。
5. 用户上传该图片对应的 4 个偏好颜色。
6. 服务端持久化颜色结果至 PostgreSQL。
7. 删除 Garnet 临时任务状态。

---

# 2. 技术选型

|模块|技术|
|-|-|
|Web Framework|ASP.NET Core Web API|
|ORM|Entity Framework Core|
|数据库|PostgreSQL|
|缓存|Garnet|
|Redis客户端|StackExchange.Redis|
|认证方式|JWT Bearer Token|
|Access Token|JWT|
|Refresh Token|数据库持久化|

---

# 3. 系统架构

```
Client
 |
 | HTTPS
 |
ASP.NET Core Web API
 |
 +----------------+
 |                |
EF Core       StackExchange.Redis
 |                |
PostgreSQL      Garnet
                 |
             临时任务状态
             TTL 10分钟
```

---

# 4. 核心业务流程


## 4.1 用户认证流程

```
新用户
 |
注册
 |
校验用户名和密码
 |
密码哈希并保存用户
 |
生成 Access Token 与 Refresh Token
 |
返回客户端

已有用户
 |
登录
 |
验证账号密码
 |
生成 Access Token 与 Refresh Token
 |
返回客户端
```

---

## 4.2 图片领取流程

```
用户请求图片
        |
        |
查询未完成图片
        |
        |
随机选择图片
        |
        |
写入 Garnet Lock
        |
        |
返回图片信息
```

Garnet Key：

```
image:lock:{imageId}
```

Value:

```json
{
    "userId":10001,
    "createdAt":"2026-08-22T10:00:00"
}
```

TTL:

```
600秒
```

---

## 4.3 图片提交流程


```
用户提交颜色
        |
        |
验证图片领取状态
        |
        |
保存颜色结果
        |
        |
更新图片状态
        |
        |
删除Garnet Lock
```


---

# 5. 数据库设计 PostgreSQL


## 5.1 用户表 Users


表名：

```
Users
```


|字段|类型|说明|
|-|-|-|
|Id|uuid|主键|
|Username|varchar(64)|用户名|
|PasswordHash|varchar(256)|密码Hash|
|CreatedAt|timestamp|创建时间|


Entity:

```csharp
public class User
{
    public Guid Id { get; set; }

    public string Username { get; set; }

    public string PasswordHash { get; set; }

    public DateTime CreatedAt { get; set; }
}
```

---

# 5.2 RefreshToken表


表名：

```
RefreshTokens
```


|字段|类型|说明|
|-|-|-|
|Id|uuid|主键|
|UserId|uuid|用户ID|
|Token|string|Refresh Token|
|ExpireAt|timestamp|过期时间|
|Revoked|boolean|是否撤销|
|CreatedAt|timestamp|创建时间|


Entity:

```csharp
public class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Token { get; set; }

    public DateTime ExpireAt { get; set; }

    public bool Revoked { get; set; }

    public DateTime CreatedAt { get; set; }
}
```

---

# 5.3 图片资源表


表名：

```
Images
```


|字段|类型|说明|
|-|-|-|
|Id|uuid|图片ID|
|Name|varchar(255)|不含扩展名的图片名|
|Data|bytea|图片地址|
|Status|int|图片状态|
|CreatedAt|timestamp|创建时间|


Status:

```
0 Pending
1 Finished
```


Entity:

```csharp
public class Image
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    public byte[] Data { get; set; }

    public ImageStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }
}
```

---

# 5.4 图片颜色结果表


表名：

```
ImageColorResults
```


|字段|类型|说明|
|-|-|-|
|Id|uuid|主键|
|ImageId|uuid|图片ID|
|UserId|uuid|用户ID|
|Color1|varchar(16)|颜色1|
|Color2|varchar(16)|颜色2|
|Color3|varchar(16)|颜色3|
|Color4|varchar(16)|颜色4|
|CreatedAt|timestamp|提交时间|


Entity:

```csharp
public class ImageColorResult
{
    public Guid Id { get; set; }

    public Guid ImageId { get; set; }

    public Guid UserId { get; set; }


    public string Color1 { get; set; }

    public string Color2 { get; set; }

    public string Color3 { get; set; }

    public string Color4 { get; set; }


    public DateTime CreatedAt { get; set; }
}
```

---

# 6. Entity Framework Core配置


## DbContext


```csharp
public class AppDbContext : DbContext
{

    public DbSet<User> Users {get;set;}

    public DbSet<Image> Images {get;set;}

    public DbSet<ImageColorResult> ImageColorResults {get;set;}

    public DbSet<RefreshToken> RefreshTokens {get;set;}


    public AppDbContext(
        DbContextOptions options)
        :base(options)
    {

    }
}
```

---

# 7. API接口设计


# 7.1 Authentication API


## 用户注册

### POST

```
/api/auth/register
```

Request:

```json
{
    "username":"test",
    "password":"12345678"
}
```

约束：

- `username` 长度为 3～64 个字符，首尾空白会被移除。
- `password` 长度为 8～128 个字符。
- 用户名必须唯一；重复注册返回 HTTP `409 Conflict`。
- 密码使用 ASP.NET Core `PasswordHasher` 哈希后存储，不保存明文密码。

Response:

```json
{
    "accessToken":"xxxxx",
    "refreshToken":"xxxxx",
    "expiresIn":900
}
```

注册成功后直接签发令牌对，无需再次登录。

---

## 用户登录


### POST

```
/api/auth/login
```


Request:

```json
{
    "username":"test",
    "password":"123456"
}
```


Response:

```json
{
    "accessToken":"xxxxx",
    "refreshToken":"xxxxx",
    "expiresIn":900
}
```

---

## 刷新Token


### POST

```
/api/auth/refresh
```


Request:

```json
{
    "refreshToken":"xxxxx"
}
```


Response:

```json
{
    "accessToken":"new-token",
    "refreshToken":"new-refresh-token"
}
```

---

## 登出


### POST

```
/api/auth/logout
```


处理：

```
RefreshToken.Revoked=true
```

---

# 7.2 图片任务 API


## 获取随机图片


### GET


```
/api/images/task
```


Header:

```
Authorization:
Bearer {accessToken}
```


Response:

```json
{
    "imageId":10001,
    "imageName":"a",
    "url":"https://server/image/a.png",
    "expireSeconds":600
}
```

---

## 提交颜色


### POST


```
/api/images/{imageId}/colors
```


Request:


```json
{
    "colors":[
        "#FF0000",
        "#00FF00",
        "#0000FF",
        "#FFFFFF"
    ]
}
```


Response:

```json
{
    "success":true
}
```

---

## 分页查询个人提交记录

### GET

```
/api/users/me/results?page=1&pageSize=20
```

`page` 从 1 开始，`pageSize` 默认为 20，最大为 100。

Response:

```json
{
    "items":[
        {
            "imageId":"89f6a53e-cf15-4ac2-b606-5cd4ec21f810",
            "imageName":"a",
            "colors":[
                "#FFFFFF",
                "#000000",
                "#FF0000",
                "#00FF00"
            ]
        }
    ],
    "page":1,
    "pageSize":20,
    "totalCount":1,
    "totalPages":1
}
```

## 分页查询全部提交记录

### GET

```
/api/users/results?page=1&pageSize=20
```

分页参数和响应结构与个人提交记录接口一致；结果按提交时间倒序返回。

---

# 8. Garnet设计


## Redis连接


使用：

```
StackExchange.Redis
```


配置：

```json
{
 "Redis":{
    "Connection":"localhost:6379"
 }
}
```


---

## Key设计


图片领取锁：

```
image:lock:{imageId}
```


Value:


```
userId
```


TTL:

```
600 seconds
```

用户当前任务：

```
user:task:{userId}
```

Value 为 `imageId`，TTL 与图片领取锁一致。用户重复请求任务时同时续期两个 Key，并返回原任务。


---

# 9. 图片领取实现逻辑


伪代码：


```csharp
var currentImageId = redis.StringGet($"user:task:{userId}");
if (currentImageId is not null && imageLockOwnedByUser(currentImageId, userId))
{
    renewImageAndUserTaskLocks();
    return currentImage;
}

while (true)
{
    var image = getRandomPendingImage();
    if (atomicallyAcquireImageAndUserTaskLocks(image.Id, userId))
    {
        return image;
    }
}
```


保证：

- 同一图片不会同时分配多个用户
- 10分钟未提交自动释放


---

# 10. JWT设计


## Access Token


有效期：

```
15分钟
```


Payload:


```json
{
    "sub":"10001",
    "username":"test",
    "exp":123456789
}
```


---

## Refresh Token


有效期：

```
7~30天
```


保存：

```
PostgreSQL RefreshTokens表
```

清理：

```text
RefreshTokenCleanupService 在服务启动时执行一次，此后每 6 小时直接删除已过期或已撤销的记录。
```


---

# 11. 项目目录结构


```
ColorPreferenceService

├── Controllers
│
│   ├── AuthController.cs
│   └── ImageController.cs
│
├── Models
│
│   ├── User.cs
│   ├── Image.cs
│   ├── ImageColorResult.cs
│   └── RefreshToken.cs
│
├── DTO
│
│   ├── LoginRequest.cs
│   ├── RegisterRequest.cs
│   ├── TokenResponse.cs
│   └── SubmitColorRequest.cs
│
├── Services
│
│   ├── JwtService.cs
│   ├── RedisService.cs
│   └── ImageTaskService.cs
│
├── Data
│
│   └── AppDbContext.cs
│
└── Program.cs
```

---

# 12. 数据一致性设计


提交颜色时采用事务：


```
BEGIN TRANSACTION

1. 验证Garnet Lock

2. 写入ImageColorResults

3. 修改Images.Status

4. 删除Redis Lock

COMMIT
```


避免：

- 提交失败导致任务丢失
- 多用户重复提交
- 图片永久锁定


---

# 13. 安全设计


## API保护

所有业务接口：

```
Authorization:
Bearer AccessToken
```


---

## Token策略


|Token|存储|有效期|
|-|-|-|
|Access Token|客户端|15分钟|
|Refresh Token|数据库|7~30天|

---

# 14. 启动 HueLab.Server

以下步骤适用于 Ubuntu 上使用 Rootless Podman，并且 PostgreSQL、Redis 均使用 `Network=host` 的部署方式。

## 14.1 启动依赖服务

安装 Redis 配置：

```bash
install -d -m 700 \
  /home/raspberry_kan/quadlet.config.d/redis \
  /home/raspberry_kan/quadlet.config.d/redis/data
install -m 600 \
  deploy/quadlet/redis.conf \
  /home/raspberry_kan/quadlet.config.d/redis/redis.conf
```

编辑配置，将 `REPLACE_WITH_REDIS_PASSWORD` 替换为高强度随机密码：

```bash
nano /home/raspberry_kan/quadlet.config.d/redis/redis.conf
```

`redis.conf` 使用 `requirepass` 强制认证。密码建议只使用足够长的字母和数字，避免空格、双引号、逗号等配置分隔字符。

Redis 当前绑定 `0.0.0.0` 且使用 host 网络。必须通过主机防火墙禁止公网访问 `6379/tcp`；若仅供本机 HueLab.Server 使用，应将 `redis.conf` 的 `bind` 改为 `127.0.0.1 -::1`。

先启动 PostgreSQL 与 Redis Quadlet：

```bash
systemctl --user daemon-reload
systemctl --user start postgres.service redis.service
```

确认两个服务均已正常运行：

```bash
systemctl --user --no-pager --full status postgres.service redis.service
podman exec huelab-postgres pg_isready --username=huelab --dbname=huelab
read -rsp 'Redis password: ' REDIS_PASSWORD; echo
podman exec --env REDISCLI_AUTH="$REDIS_PASSWORD" huelab-redis redis-cli ping
unset REDIS_PASSWORD
```

Redis 检查应返回：

```text
PONG
```

## 14.2 配置并构建 HueLab.Server 镜像

HueLab.Server 使用镜像内部的 `HueLab.Server/appsettings.json`，不从 Quadlet 加载 `server.env`。构建镜像前编辑该文件：

```bash
nano HueLab.Server/appsettings.json
```

必须检查并修改：

- `ConnectionStrings:Default`：数据库名、用户名和密码必须与 PostgreSQL 的 `postgres.env` 一致。
- `Redis:Connection`：格式为 `localhost:6379,password=实际Redis密码`，密码必须与 `redis.conf` 的 `requirepass` 完全一致。
- `Jwt:Key`：必须替换为至少 32 字节的高强度随机值。
- `Jwt:Issuer`、`Jwt:Audience`：必须与客户端令牌验证配置一致。

配置会被复制进容器镜像。不得将含真实生产密码和 JWT 密钥的镜像推送到公开镜像仓库。

在解决方案根目录构建：

```bash
podman build \
  --pull=newer \
  --tag localhost/huelab-server:latest \
  --file HueLab.Server/Dockerfile \
  HueLab.Server
```

## 14.3 安装 HueLab.Server Quadlet

安装 Quadlet 文件：

```bash
install -d -m 700 ~/.config/containers/systemd
install -m 644 \
  deploy/quadlet/huelab-server.container \
  ~/.config/containers/systemd/huelab-server.container
```

`huelab-server.container` 使用 `Network=host`，直接读取镜像内部的 `appsettings.json`，并声明依赖 `postgres.container` 与 `redis.container`。服务启动失败时，systemd 每隔 5 秒重新启动；这也能处理 PostgreSQL 尚未完成初始化的短暂启动竞争。

## 14.4 启动 HueLab.Server

重新加载用户级 systemd 配置并启动服务：

```bash
systemctl --user daemon-reload
systemctl --user start huelab-server.service
```

HueLab.Server 启动时会自动执行 Entity Framework Core 数据库迁移。其 Quadlet 会同时拉起 PostgreSQL 与 Redis 依赖。

若要求退出 SSH 会话后继续运行，并在主机重启后自动启动，执行一次：

```bash
sudo loginctl enable-linger raspberry_kan
```

检查配置：

```bash
loginctl show-user raspberry_kan --property=Linger
```

预期结果：

```text
Linger=yes
```

## 14.5 检查服务

查看服务状态：

```bash
systemctl --user --no-pager --full status \
  postgres.service \
  redis.service \
  huelab-server.service
```

持续查看 HueLab.Server 日志：

```bash
journalctl --user -u huelab-server.service --follow
```

检查 OpenAPI：

```bash
curl --fail http://127.0.0.1:8080/openapi/v1.json
```

Scalar API 页面：

```text
http://127.0.0.1:8080/scalar/v1
```

默认配置只监听 `127.0.0.1`，不会直接向局域网或公网开放。需要远程访问时，应通过 Caddy、Nginx 等反向代理提供 HTTPS，而不是直接暴露应用端口。

## 14.6 更新、重启与停止

代码或内部 `appsettings.json` 更新后，在解决方案根目录重新构建镜像：

```bash
podman build \
  --pull=newer \
  --tag localhost/huelab-server:latest \
  --file HueLab.Server/Dockerfile \
  HueLab.Server
```

重新创建并启动使用新镜像的服务容器：

```bash
systemctl --user restart huelab-server.service
```

停止或启动：

```bash
systemctl --user stop huelab-server.service
systemctl --user start huelab-server.service
```

查看最近 200 行日志：

```bash
journalctl --user -u huelab-server.service --lines 200 --no-pager
```