// Atmosphere.cs - the day sky and sun with soft shadows, fog, and weather
// particles (rain, snow, drifting fog) that follow the camera, pushed by
// one wind. Set up once per game from the map's climate and the options.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class Atmosphere
    {
        public Light Sun { get; private set; }
        public WeatherChoice Weather { get; private set; }
        public Vector3 Wind = new Vector3(1.5f, 0, 0.6f);
        Vector3 blown = new Vector3(float.NaN, 0f, 0f);
        GameObject root;
        ParticleSystem particles;
        Material skybox, particleMat;
        Volume post;
        VolumeProfile profile;
        Texture2D dot;

        public void Build(Transform parent, string climate, WeatherChoice weather, bool shadows, float mapSize)
        {
            root = new GameObject("Atmosphere");
            root.transform.SetParent(parent, false);

            Sun = new GameObject("Sun").AddComponent<Light>();
            Sun.transform.SetParent(root.transform, false);
            Sun.type = LightType.Directional;
            Sun.transform.rotation = Quaternion.Euler(48f, 150f, 0f);
            Sun.color = climate == "desert" ? new Color(1f, 0.93f, 0.8f) : climate == "snow" ? new Color(0.92f, 0.95f, 1f) : new Color(1f, 0.96f, 0.88f);
            Sun.intensity = 1.0f;
            Sun.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            Sun.shadowStrength = 0.8f;
            RenderSettings.sun = Sun;
            QualitySettings.shadows = UnityEngine.ShadowQuality.All;
            QualitySettings.shadowResolution = UnityEngine.ShadowResolution.VeryHigh;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            QualitySettings.shadowCascades = 4;
            QualitySettings.shadowCascade4Split = new Vector3(0.08f, 0.22f, 0.5f);
            // The original models are small, a unit is about two cells tall,
            // so the biases stay tight to keep feet on their shadows.
            Sun.shadowBias = 0.02f;
            Sun.shadowNormalBias = 0.25f;
            Sun.shadowNearPlane = 0.2f;

            var sky = Shader.Find("Skybox/Procedural");
            if (sky != null)
            {
                skybox = new Material(sky) { hideFlags = HideFlags.DontSave };
                skybox.SetFloat("_SunSize", 0.03f);
                skybox.SetFloat("_AtmosphereThickness", climate == "desert" ? 1.2f : 0.9f);
                skybox.SetColor("_SkyTint", new Color(0.5f, 0.55f, 0.62f));
                skybox.SetColor("_GroundColor", new Color(0.36f, 0.38f, 0.36f));
                skybox.SetFloat("_Exposure", 1.2f);
                RenderSettings.skybox = skybox;
            }
            // Ambient light comes from the sky itself.
            RenderSettings.ambientMode = skybox != null ? UnityEngine.Rendering.AmbientMode.Skybox : UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientIntensity = 0.85f;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.5f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.26f, 0.22f);
            DynamicGI.UpdateEnvironment();

            SetWeather(GameOptions.Resolve(weather, climate));
            BuildPost(climate);
            BuildReflections(mapSize);
        }

        // Bloom, colour grading, tonemapping and a soft vignette, under URP.
        // Ambient occlusion is a renderer feature on the pipeline asset.
        void BuildPost(string climate)
        {
            if (Looks.Urp == null) return;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var grade = profile.Add<ColorAdjustments>(true);
            grade.contrast.Override(10f);
            grade.saturation.Override(climate == "snow" ? 0f : 8f);
            grade.postExposure.Override(0.1f);
            var balance = profile.Add<WhiteBalance>(true);
            balance.temperature.Override(climate == "snow" ? -8f : climate == "desert" ? 10f : 4f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.5f);
            post = new GameObject("Post Effects").AddComponent<Volume>();
            post.transform.SetParent(root.transform, false);
            post.isGlobal = true;
            post.sharedProfile = profile;
        }

        // The sky as every model's reflections, so metals and gems read as
        // metal and gem. One probe renders only the sky, a face a frame, so
        // it follows the sun and the weather for next to nothing.
        public ReflectionProbe Reflections { get; private set; }

        void BuildReflections(float mapSize)
        {
            Reflections = new GameObject("Sky reflections").AddComponent<ReflectionProbe>();
            Reflections.transform.SetParent(root.transform, false);
            Reflections.transform.position = new Vector3(mapSize / 2, 0, -mapSize / 2);
            Reflections.mode = ReflectionProbeMode.Realtime;
            Reflections.refreshMode = ReflectionProbeRefreshMode.EveryFrame;
            Reflections.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            Reflections.clearFlags = ReflectionProbeClearFlags.Skybox;
            Reflections.cullingMask = 0;
            Reflections.resolution = 128;
            Reflections.hdr = true;
            Reflections.size = Vector3.one * 100000f;
            Reflections.intensity = 1f;
            Reflections.importance = 0;
        }

        public void SetPostEffects(bool on, Camera cam)
        {
            if (post != null) post.enabled = on;
            if (cam != null && Looks.Urp != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = on;
                data.antialiasing = on ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
            }
        }

        public void SetWeather(WeatherChoice w)
        {
            Weather = w;
            blown = new Vector3(float.NaN, 0f, 0f);
            if (particles != null) { Looks.Release(particles.gameObject); particles = null; }
            RenderSettings.fog = w == WeatherChoice.Fog || w == WeatherChoice.Snow || w == WeatherChoice.Rain;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = w == WeatherChoice.Snow ? new Color(0.82f, 0.85f, 0.9f) : new Color(0.62f, 0.66f, 0.7f);
            RenderSettings.fogStartDistance = w == WeatherChoice.Fog ? 20f : 60f;
            RenderSettings.fogEndDistance = w == WeatherChoice.Fog ? 110f : 220f;
            if (Sun != null) Sun.intensity = w == WeatherChoice.Off ? 1.0f : w == WeatherChoice.Snow ? 0.85f : 0.7f;
            if (w == WeatherChoice.Off || w == WeatherChoice.ByMap) return;

            var go = new GameObject("Weather");
            go.transform.SetParent(root.transform, false);
            particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = w == WeatherChoice.Rain ? 6000 : 3000;
            var emission = particles.emission;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(90, 1, 90);
            var vel = particles.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            var noise = particles.noise;
            var r = particles.GetComponent<ParticleSystemRenderer>();

            if (particleMat == null)
            {
                dot = UI.UiKit.Glow.texture;
                // The effect shader is unlit, alpha blended and takes the
                // particles' colours, in either pipeline.
                particleMat = new Material(Looks.Find("OkuEffect", "Sprites/Default")) { hideFlags = HideFlags.DontSave, mainTexture = dot };
            }
            r.sharedMaterial = particleMat;

            switch (w)
            {
                case WeatherChoice.Rain:
                    main.startLifetime = 1.2f;
                    main.startSpeed = 0;
                    main.startSize = 0.06f;
                    main.startColor = new Color(0.75f, 0.8f, 0.9f, 0.55f);
                    emission.rateOverTime = 4500;
                    vel.x = Wind.x; vel.y = -26f; vel.z = Wind.z;
                    r.renderMode = ParticleSystemRenderMode.Stretch;
                    r.velocityScale = 0.05f;
                    r.lengthScale = 1f;
                    break;
                case WeatherChoice.Snow:
                    main.startLifetime = 9f;
                    main.startSpeed = 0;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
                    main.startColor = new Color(1, 1, 1, 0.9f);
                    emission.rateOverTime = 700;
                    vel.x = Wind.x * 0.6f; vel.y = -2.4f; vel.z = Wind.z * 0.6f;
                    noise.enabled = true;
                    noise.strength = 0.8f;
                    noise.frequency = 0.3f;
                    r.renderMode = ParticleSystemRenderMode.Billboard;
                    break;
                default:
                    main.startLifetime = 14f;
                    main.startSpeed = 0;
                    main.startSize = new ParticleSystem.MinMaxCurve(10f, 18f);
                    main.startColor = new Color(0.85f, 0.87f, 0.9f, 0.06f);
                    emission.rateOverTime = 18;
                    vel.x = Wind.x * 0.3f; vel.y = 0; vel.z = Wind.z * 0.3f;
                    shape.scale = new Vector3(110, 4, 110);
                    r.renderMode = ParticleSystemRenderMode.Billboard;
                    break;
            }
            particles.Play();
        }

        // The battle's wind, world units a second along the ground: the rain,
        // snow and drifting fog follow it as the smoke does, turning with it
        // over a second or two. The sea keeps its own.
        public void Blow(Vector3 wind)
        {
            wind.y = 0f;
            if (particles == null) return;
            var to = float.IsNaN(blown.x) ? wind : Vector3.MoveTowards(blown, wind, 1.5f * Mathf.Max(0.001f, Time.unscaledDeltaTime));
            if (!float.IsNaN(blown.x) && (to - blown).sqrMagnitude < 1e-6f) return;
            blown = to;
            wind = to;
            var vel = particles.velocityOverLifetime;
            float share = Weather == WeatherChoice.Rain ? 1f : Weather == WeatherChoice.Snow ? 0.6f : 0.3f;
            vel.x = wind.x * share;
            vel.z = wind.z * share;
        }

        // Keeps the weather box over what the camera looks at.
        public void Follow(Vector3 focus, float cameraHeight, float cameraDistance)
        {
            // The sun and sky for shaders that light themselves, such as the
            // land past the edge.
            if (Sun != null)
            {
                Shader.SetGlobalVector("_OkuSunDir", -Sun.transform.forward);
                Shader.SetGlobalVector("_OkuSunColor", Sun.color * Sun.intensity);
                Shader.SetGlobalVector("_OkuAmbient", RenderSettings.ambientSkyColor * 0.55f);
            }
            // Haze thickens past what the camera frames, however far out it
            // is, and in clear weather only far off, so the land past the
            // edge melts into it and the sky meets it at the horizon.
            bool fog = Weather == WeatherChoice.Fog, clear = Weather == WeatherChoice.Off || Weather == WeatherChoice.ByMap;
            RenderSettings.fog = true;
            RenderSettings.fogStartDistance = cameraDistance * (fog ? 0.8f : clear ? 2.2f : 1.4f);
            RenderSettings.fogEndDistance = cameraDistance * (fog ? 3.2f : clear ? 9f : 6f) + 60f;
            Shader.SetGlobalColor("_OkuHaze", RenderSettings.fogColor);
            if (particles == null) return;
            float lift = Weather == WeatherChoice.Fog ? 2f : Mathf.Min(cameraHeight, 30f) + 4f;
            particles.transform.position = new Vector3(focus.x, focus.y + lift, focus.z);
        }

        public void Dispose()
        {
            if (root != null) Looks.Release(root);
            if (skybox != null) Looks.Release(skybox);
            if (particleMat != null) Looks.Release(particleMat);
            if (profile != null) Looks.Release(profile);
            RenderSettings.fog = false;
        }
    }
}
