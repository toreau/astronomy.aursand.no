FROM mcr.microsoft.com/dotnet/sdk:10.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends gcc libc6-dev make tcsh \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /spice
COPY vendor/cspice /spice/cspice
COPY fixtures/kernels/de440s_a.bsp /spice/kernels/de440s.bsp
COPY vendor/cspice/kernels/lsk/latest_leapseconds.tls /spice/kernels/latest_leapseconds.tls
COPY vendor/cspice/kernels/pck/pck00010.tpc /spice/kernels/pck00010.tpc

WORKDIR /spice/cspice/src
RUN mkdir -p ../lib ../exe \
    && sed -i '3252,3255d' ../include/SpiceZpr.h \
    && csh mk_linux.csh && ls -la /spice/cspice/lib/

WORKDIR /spice
RUN gcc -shared -fPIC -o /spice/libcspice.so \
        -Wl,--whole-archive /spice/cspice/lib/libcspice.a \
        -Wl,--no-whole-archive -lm \
    && ls -la /spice/libcspice.so

WORKDIR /probe
COPY probe /probe
RUN dotnet publish -c Release -o /probe/out \
    && cp /spice/libcspice.so /probe/out/

CMD ["dotnet", "/probe/out/Probe.dll", "/spice/kernels/de440s.bsp", "/spice/kernels/latest_leapseconds.tls", "/spice/kernels/pck00010.tpc"]
