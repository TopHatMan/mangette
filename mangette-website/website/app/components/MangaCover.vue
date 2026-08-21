<template>
    <div
        class="relative object-contain max-sm:w-[var(--mangacover-width-sm)] max-sm:h-[var(--mangacover-height-sm)] w-(--mangacover-width) h-(--mangacover-height) rounded-lg overflow-clip">
        <div
            v-if="blur"
            class="absolute l-0 t-0 w-full h-full rounded-lg overflow-clip"
            style="
                background: linear-gradient(150deg, rgba(24, 24, 27, 0.55) 40%, rgba(99, 102, 241, 0.2));
                box-shadow: 0 4px 30px rgba(0, 0, 0, 0.25);
                backdrop-filter: blur(2px) brightness(70%);
                -webkit-backdrop-filter: blur(2px) brightness(70%);
            ">
            <p class="p-3 max-sm:text-sm text-xl font-semibold max-h-full overflow-clip text-shadow-lg text-white">
                {{ manga?.name }}
            </p>
        </div>
        <img
            :src="`${$config.public.openFetch.api.baseURL}v2/Manga/${manga.key}/Cover/Medium`"
            class="w-full h-full object-cover"
            alt="" />
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type Manga = components['schemas']['Manga'];
type MinimalManga = components['schemas']['MinimalManga'];

defineProps<{ manga: Manga | MinimalManga; blur?: boolean }>();
</script>
