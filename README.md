# kmax-presentation

Hands-off Presentation Scene for the KMax launcher: a random carousel of every developed model, one bottom-centre information panel, no buttons.

- `Scripts/Runtime/PresentationDirector.cs`: carousel, smart random order, show-offs, double-sided materials, info panel.
- `Scripts/Editor/PresentationSceneBuilder.cs`: menu `KMax/Presentation/Build Presentation Scene` rebuilds `Scenes/PresentationScene.unity` (catalog, lighting, camera, environment, backdrop, UI).
- `Environment/`: Virtual Showroom Space (Sketchfab), parts arranged in Blender (`Source~/Showroom_Arranged.blend` (not imported by Unity)) and exported as `Showroom_Arranged.fbx`, with the materials and textures the builder assigns by part name.
- `Backdrop/`: perspective-grid wallpaper recoloured to the Design.md backdrop slate #252A36.
- `Prefabs/PresentationAircraft.prefab`: biplane with wind streams, extracted from the AircraftFlight experience.
- `docs/11-presentation-copy.md`: titles, sub-titles and descriptions.

Depends on: kmax-solar-system (MissionControlSceneBuilder UI helpers), kmax-display-example, kmax-education, kmax-human-anatomy (by reflection), kmax-retail-products, KMaxPCBAssembly, kmax-automobile-engine.
