FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# SvelteSharp のツールチェーン（esbuild等）のビルド時実行用に Node.js をインストール
RUN apt-get update && apt-get install -y nodejs && rm -rf /var/lib/apt/lists/*

WORKDIR /src

# 必要なプロジェクトファイルをコピーして依存関係を復元
COPY server/BalloonMesh.Server.csproj server/
COPY server/BalloonMesh.Protocol/BalloonMesh.Protocol.csproj server/BalloonMesh.Protocol/
COPY third_party/SvelteSharp/SvelteSharp.Core/SvelteSharp.Core.csproj third_party/SvelteSharp/SvelteSharp.Core/
COPY third_party/SvelteSharp/SvelteSharp.Jint/SvelteSharp.Jint.csproj third_party/SvelteSharp/SvelteSharp.Jint/
COPY third_party/SvelteSharp/SvelteSharp.Build/SvelteSharp.Build.csproj third_party/SvelteSharp/SvelteSharp.Build/

RUN dotnet restore server/BalloonMesh.Server.csproj

# ソースコードをコピー
COPY server/ server/
COPY third_party/SvelteSharp/ third_party/SvelteSharp/

# SvelteSharp.Build を先にビルド（BalloonMesh.Server がビルドタスクとして参照するため）
RUN dotnet build third_party/SvelteSharp/SvelteSharp.Build/SvelteSharp.Build.csproj -c Release

# BalloonMesh.Server を publish
WORKDIR /src/server
RUN dotnet publish BalloonMesh.Server.csproj -c Release -o /app/publish /p:UseAppHost=false

# 実行環境
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# SvelteSharp ツールチェーンと Views をコピー
COPY --from=build /src/server/tools/svelte ./tools/svelte
COPY --from=build /src/server/Views ./Views

ENV ASPNETCORE_URLS=http://+:5080
EXPOSE 5080

ENTRYPOINT ["dotnet", "BalloonMesh.Server.dll"]
