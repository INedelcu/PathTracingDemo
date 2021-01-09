# Path Tracing Demo

This demo implements a Monte Carlo Path Tracing technique using hardware accelerated ray tracing support in Unity.

<img src="Images/397D40F8-9241-49FD-8063-1386E574D217.png" width="1280">

## Prerequisites

* Windows 10 version 1809 and above.
* Any NVIDIA GPU starting from GTX 1060 with 6 GB of VRAM. AMD 6000 series GPUs based on RDNA 2 architecture might also run the demo but were not tested.
* Unity 2020.2 and above.

## Recommendations

* Use **winver** in a command prompt to see exactly which Windows version you are using.
* NVIDIA GPUs from RTX 2000 series and above are recommended because they have hardware units dedicated to ray tracing. A GTX 1060 GPU was 10 to 15 times slower in tests because ray tracing support is emulated.
* Alternatively use a tool like GPU-Z to check if your system supports ray tracing.

## Acknowledgements

Alan Wolfe (@Atrix256 on Twitter) for his blog about computer graphics. The demo was inspired by the series of 3 blog posts:

https://blog.demofox.org/2020/05/25/casual-shadertoy-path-tracing-1-basic-camera-diffuse-emissive/
https://blog.demofox.org/2020/06/06/casual-shadertoy-path-tracing-2-image-improvement-and-glossy-reflections/
https://blog.demofox.org/2020/06/14/casual-shadertoy-path-tracing-3-fresnel-rough-refraction-absorption-orbit-camera/
