FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ .
RUN dotnet restore Astronomy.DataIngestion/Astronomy.DataIngestion.csproj
RUN dotnet publish Astronomy.DataIngestion/Astronomy.DataIngestion.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS cspice
RUN apt-get update \
    && apt-get install -y --no-install-recommends gcc libc6-dev make tcsh meson ninja-build \
    && rm -rf /var/lib/apt/lists/*
RUN git clone --depth 1 --filter=blob:none --sparse https://github.com/arturania/cspice.git /spice-src \
    && cd /spice-src && git sparse-checkout set src include lib
WORKDIR /spice-src/src
RUN mkdir -p ../lib ../exe \
    && sed -i '3252,3255d' ../include/SpiceZpr.h \
    && csh mk_linux.csh
WORKDIR /spice-src
RUN gcc -shared -fPIC -o /libcspice.so \
        -Wl,--whole-archive /spice-src/lib/libcspice.a \
        -Wl,--no-whole-archive -lm

RUN git clone --depth 1 https://github.com/liberfa/erfa.git /erfa-src \
    && meson setup /erfa/build /erfa-src \
    && ninja -C /erfa/build \
    && find /erfa/build/src -name 'liberfa.so.1*' -exec cp {} /liberfa.so \;

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /out .
COPY --from=cspice /libcspice.so /app/
COPY --from=cspice /liberfa.so /app/
ENV ASTRONOMY_KERNEL_PATH=/data/kernels
ENTRYPOINT ["dotnet", "Astronomy.DataIngestion.dll", "heartbeat"]
