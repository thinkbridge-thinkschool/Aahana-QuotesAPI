FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY src/ ./src/
RUN dotnet restore src/Host/OrderFulfillment.Api/OrderFulfillment.Api.csproj

RUN dotnet publish src/Host/OrderFulfillment.Api/OrderFulfillment.Api.csproj -c Release -o /app/publish --no-restore

# Day 29: confirmed live — Alpine's .NET runtime image defaults to Globalization Invariant Mode
# (no ICU libs, to keep the image small), and Microsoft.Data.SqlClient's
# "Authentication=Active Directory Default" needs full globalization support internally (it
# threw "System.NotSupportedException: Globalization Invariant Mode is not supported" opening
# the very first connection to Azure SQL). QuotesApi's own Dockerfile never hits this because it
# only ever uses SQLite/local SQL auth, never AAD-based SqlClient auth, in its deployed container.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderFulfillment.Api.dll"]
