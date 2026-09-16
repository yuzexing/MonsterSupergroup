// Nordic gameplay is an XY plane at Z=0. Preserve authored XY geometry after
// animation/rotation; only eliminate perspective depth magnification.
#ifndef GAMEPLAY_PLANAR_PROJECTION
#define GAMEPLAY_PLANAR_PROJECTION
float3 GameplayPlanarWorld(float3 worldPosition) { return float3(worldPosition.xy, 0.0); }
#endif
