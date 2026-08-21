<template>
    <div class="arr-page">
        <div class="arr-page__head">
            <div>
                <h1 class="arr-page__title">Add New</h1>
                <p class="arr-page__hint">Search sites, pick one series, then add it. Nothing is saved until you click Add.</p>
            </div>
        </div>

        <form class="flex flex-col gap-3 mb-5" @submit.prevent="performSearch">
            <UInput
                v-model="query"
                size="xl"
                icon="i-lucide-search"
                placeholder="Series title or site URL, e.g. One Piece"
                autofocus
                :disabled="busy" />
            <div class="flex flex-wrap gap-1">
                <UButton
                    size="sm"
                    :color="!connectorName ? 'primary' : 'neutral'"
                    :variant="!connectorName ? 'solid' : 'outline'"
                    :disabled="busy"
                    @click="connectorName = ''">
                    All sites
                </UButton>
                <UButton
                    v-for="c in siteConnectors"
                    :key="c.key"
                    size="sm"
                    :color="connectorName === c.name ? 'primary' : 'neutral'"
                    :variant="connectorName === c.name ? 'solid' : 'outline'"
                    :disabled="busy"
                    @click="selectConnector(c.name)">
                    {{ c.name }}
                </UButton>
                <UButton type="submit" class="ml-auto" :loading="busy" :disabled="!query?.trim()">Search</UButton>
            </div>
        </form>

        <p v-if="searched && !busy && hits.length === 0" class="text-muted">No results for “{{ searchQuery }}”.</p>
        <p v-else-if="hits.length" class="text-muted text-sm mb-3">
            {{ hits.length }} result<span v-if="hits.length !== 1">s</span> for
            <span class="text-highlighted">{{ searchQuery }}</span>
            — choose one to add.
        </p>

        <div class="flex flex-col gap-2">
            <div v-for="hit in hits" :key="`${hit.connectorName}:${hit.idOnSite}`" class="arr-result">
                <img :src="coverSrc(hit.coverUrl)" alt="" class="arr-result__poster" />
                <div class="min-w-0 grow">
                    <div class="flex flex-wrap items-baseline gap-2">
                        <p class="font-semibold text-lg truncate">{{ hit.name }}</p>
                        <span v-if="hit.year" class="text-muted text-sm">{{ hit.year }}</span>
                        <UBadge size="sm" variant="subtle" color="neutral">{{ hit.connectorName }}</UBadge>
                        <UBadge v-if="hit.releaseStatus" size="sm" variant="outline">{{ hit.releaseStatus }}</UBadge>
                    </div>
                    <p class="text-muted text-sm line-clamp-3 mt-1">{{ hit.description || 'No overview.' }}</p>
                </div>
                <div class="shrink-0 flex flex-col gap-2 items-stretch min-w-32">
                    <UButton v-if="hit.alreadyInLibrary && hit.existingMangaId" :to="`/manga/${hit.existingMangaId}`" variant="outline">
                        Already added
                    </UButton>
                    <UButton v-else :loading="addingKey === hitKey(hit)" @click="openAdd(hit)">Add</UButton>
                </div>
            </div>
        </div>

        <UModal v-model:open="addOpen" title="Add Series">
            <template #body>
                <div v-if="pending" class="flex flex-col gap-4">
                    <div class="flex gap-3">
                        <img :src="coverSrc(pending.coverUrl)" alt="" class="w-24 h-36 object-cover rounded-md" />
                        <div class="min-w-0">
                            <p class="font-semibold text-lg">{{ pending.name }}</p>
                            <p class="text-muted text-sm">{{ pending.connectorName }}<span v-if="pending.year"> · {{ pending.year }}</span></p>
                            <p class="text-muted text-sm line-clamp-4 mt-2">{{ pending.description }}</p>
                        </div>
                    </div>
                    <UFormField label="Root folder">
                        <USelect v-model="libraryId" :items="libraryItems" class="w-full" />
                    </UFormField>
                    <UCheckbox v-model="monitor" label="Monitor and download missing chapters" />
                    <p class="text-muted text-xs">
                        Only this series is added. Other search hits stay off the library until you add them.
                    </p>
                    <p v-if="addError" class="text-error text-sm">{{ addError }}</p>
                </div>
            </template>
            <template #footer>
                <div class="flex justify-end gap-2">
                    <UButton variant="ghost" @click="addOpen = false">Cancel</UButton>
                    <UButton :loading="adding" @click="confirmAdd">Add Series</UButton>
                </div>
            </template>
        </UModal>
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type MangaConnector = components['schemas']['MangaConnector'];
type FileLibrary = components['schemas']['FileLibrary'];
type SearchHit = {
    name: string;
    description: string;
    year?: number | null;
    releaseStatus: string;
    coverUrl: string;
    connectorName: string;
    idOnSite: string;
    websiteUrl?: string | null;
    score: number;
    alreadyInLibrary: boolean;
    existingMangaId?: string | null;
};
type AddResult = { key: string };

const { $api } = useNuxtApp();
const { data: connectors } = await useApi('/v2/MangaConnector', { key: FetchKeys.MangaConnector.All, server: false });
const { data: libraries } = await useApi('/v2/FileLibrary', { key: FetchKeys.FileLibraries, server: false });

const siteConnectors = computed(() => (connectors.value ?? []).filter((c: MangaConnector) => c.name !== 'Global' && c.enabled !== false));
const libraryItems = computed(() =>
    (libraries.value ?? []).map((l: FileLibrary) => ({ label: `${l.libraryName} (${l.basePath})`, value: l.key })),
);

const query = ref('');
const connectorName = ref('');
const busy = ref(false);
const searched = ref(false);
const searchQuery = ref('');
const hits = ref<SearchHit[]>([]);
const addOpen = ref(false);
const pending = ref<SearchHit | null>(null);
const libraryId = ref<string | undefined>();
const monitor = ref(true);
const adding = ref(false);
const addingKey = ref('');
const addError = ref('');

const hitKey = (h: SearchHit) => `${h.connectorName}:${h.idOnSite}`;
const coverSrc = (url: string) => (url ? `/v2/Search/Cover?url=${encodeURIComponent(url)}` : '');

const selectConnector = (name: string) => {
    connectorName.value = name;
};

const performSearch = async () => {
    const q = query.value?.trim();
    if (!q) return;
    busy.value = true;
    searched.value = true;
    searchQuery.value = q;
    hits.value = [];
    try {
        const params = new URLSearchParams({ query: q });
        if (connectorName.value) params.set('connectorName', connectorName.value);
        hits.value = await $fetch<SearchHit[]>(`/v2/Search/Lookup?${params.toString()}`);
    } catch {
        hits.value = [];
    } finally {
        busy.value = false;
    }
};

const openAdd = (hit: SearchHit) => {
    pending.value = hit;
    addError.value = '';
    monitor.value = true;
    libraryId.value = libraries.value?.[0]?.key;
    addOpen.value = true;
};

const confirmAdd = async () => {
    if (!pending.value) return;
    adding.value = true;
    addingKey.value = hitKey(pending.value);
    addError.value = '';
    try {
        const added = await $fetch<AddResult>('/v2/Search/Add', {
            method: 'POST',
            body: {
                connectorName: pending.value.connectorName,
                idOnSite: pending.value.idOnSite,
                libraryId: libraryId.value,
                monitor: monitor.value,
            },
        });
        addOpen.value = false;
        await refreshNuxtData(FetchKeys.Manga.All);
        if (added?.key) await navigateTo(`/manga/${added.key}`);
        else await navigateTo('/');
    } catch (e: unknown) {
        addError.value = e instanceof Error ? e.message : 'Could not add that series.';
    } finally {
        adding.value = false;
        addingKey.value = '';
    }
};

useHead({ title: 'Add New' });
</script>
