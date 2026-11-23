# Build from the repository root with --platform=linux/amd64.
# These are verified linux/amd64 children of the exact Microsoft Noble tags.
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:0eeb52c76e35a5431ca707ad2bc75e38006a05393045d8532ae44c15d9474523 AS build
WORKDIR /source
COPY . .
WORKDIR /source/implementations/csharp
RUN dotnet restore applications/CIS.Management.Service/CIS.Management.Service.csproj --locked-mode \
    && dotnet publish applications/CIS.Management.Service/CIS.Management.Service.csproj --no-restore -c Release -r linux-x64 --self-contained false -p:UseAppHost=false -o /output/service \
    && dotnet restore tools/CisHealthProbe/CisHealthProbe.csproj --locked-mode \
    && dotnet publish tools/CisHealthProbe/CisHealthProbe.csproj --no-restore -c Release --self-contained false -p:UseAppHost=false -o /output/probe

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble@sha256:0fa044f682cb7d93a5a90401a00c626c66f7b00b86922be9441f869eae039f80 AS runtime
WORKDIR /app
COPY --from=build /output/service/ /app/service/
COPY --from=build /output/probe/ /app/probe/
ENV CIS_HEALTHCHECK_PIPE_ENABLED=true \
    CIS_HEALTHCHECK_PIPE_NAME=management-health
USER $APP_UID
EXPOSE 7443
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD ["dotnet", "/app/probe/CisHealthProbe.dll", "--pipe", "management-health", "--view", "readiness", "--timeout-ms", "1000", "--max-report-bytes", "4194304"]
ENTRYPOINT ["dotnet", "/app/service/CIS.Management.Service.dll"]
CMD ["--settings", "/run/cis/settings.json"]
