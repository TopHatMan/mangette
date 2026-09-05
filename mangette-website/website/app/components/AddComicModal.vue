<template>
    <UModal v-bind="$props" title="Add Comic" :ui="{ content: 'max-w-2xl' }">
        <template #body>
            <div v-if="step === 'search'" class="flex flex-col gap-3">
                <p class="text-muted text-sm">
                    Search ComicVine and pick the exact volume/run you want -- "Batman v1 (1940)", "Batman (2011) New
                    52", and "Batman (2016) Rebirth" are different volumes, so pick the one that matches what you're
                    actually watching for.
                </p>
                <form class="flex gap-2" @submit.prevent="runSearch">
                    <UInput v-model="query" class="w-full" placeholder="Series title, e.g. Batman" autofocus :disabled="searching" />
                    <UButton type="submit" :loading="searching" :disabled="!query.trim()">Search</UButton>
                </form>
                <p v-if="searchError" class="text-error text-sm">{{ searchError }}</p>
                <p v-else-if="searched && !searching && hits.length === 0" class="text-muted text-sm">
                    No ComicVine results for "{{ searchedFor }}".
                </p>
                <div v-if="hits.length" class="flex flex-col gap-2 max-h-96 overflow-y-auto">
                    <div v-for="hit in hits" :key="hit.comicVineVolumeId" class="flex gap-3 bg-elevated rounded-lg p-2">
                        <img :src="hit.coverUrl ?? ''" alt="" class="w-14 h-20 object-cover rounded shrink-0 bg-muted" />
                        <div class="min-w-0 grow">
                            <div class="flex flex-wrap items-baseline gap-2">
                                <p class="font-semibold truncate">{{ hit.name }}</p>
                                <span v-if="hit.year" class="text-muted text-sm">{{ hit.year }}</span>
                                <UBadge v-if="hit.issueCount" size="sm" variant="subtle" color="neutral">{{ hit.issueCount }} issues</UBadge>
                            </div>
                            <p class="text-muted text-xs">{{ hit.publisher }}</p>
                            <p class="text-muted text-xs line-clamp-2 mt-1">{{ hit.description }}</p>
                        </div>
                        <UButton size="sm" class="shrink-0 self-center" @click="pick(hit)">Select</UButton>
                    </div>
                </div>
                <UButton variant="ghost" size="sm" class="w-fit" @click="pickManual">Can't find it? Add by title only</UButton>
            </div>

            <div v-else class="flex flex-col gap-3">
                <div v-if="selected" class="flex gap-3">
                    <img v-if="selected.coverUrl" :src="selected.coverUrl" alt="" class="w-16 h-24 object-cover rounded shrink-0" />
                    <div class="min-w-0">
                        <p class="font-semibold">{{ selected.name }}</p>
                        <p class="text-muted text-sm">
                            {{ selected.publisher }}<span v-if="selected.year"> · {{ selected.year }}</span
                            ><span v-if="selected.issueCount"> · {{ selected.issueCount }} issues</span>
                        </p>
                    </div>
                </div>
                <UFormField v-else label="Series title" required>
                    <UInput v-model="manualName" placeholder="Amazing Spider-Man" class="w-full" />
                </UFormField>
                <UFormField label="Library" required>
                    <USelect
                        v-model="fileLibraryId"
                        :items="comicLibraries?.map((l) => ({ label: `${l.libraryName} (${l.basePath})`, value: l.key })) ?? []"
                        placeholder="Pick a Comic-kind library"
                        class="w-full" />
                </UFormField>
                <div class="grid grid-cols-2 gap-2">
                    <UFormField label="First issue #" required>
                        <UInput v-model.number="issueStart" type="number" min="1" class="w-full" />
                    </UFormField>
                    <UFormField label="Last issue #" :hint="selected?.issueCount ? 'Defaults to ComicVine\'s issue count' : 'Blank = ongoing'">
                        <UInput v-model.number="issueEnd" type="number" min="1" class="w-full" />
                    </UFormField>
                </div>
                <p v-if="addMessage" class="text-error text-sm">{{ addMessage }}</p>
                <div class="flex gap-2">
                    <UButton variant="ghost" @click="backToSearch">Back</UButton>
                    <UButton :loading="adding" :disabled="!fileLibraryId || (!selected && !manualName.trim())" @click="confirmAdd">
                        Add
                    </UButton>
                </div>
            </div>
        </template>
    </UModal>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type ComicVineVolumeSummary = components['schemas']['ComicVineVolumeSummary'];

const { $api } = useNuxtApp();

const { data: fileLibraries } = useApi('/v2/FileLibrary', { key: FetchKeys.FileLibraries, server: false });
const comicLibraries = computed(() => fileLibraries.value?.filter((l) => l.kind === 'Comic') ?? []);

const step = ref<'search' | 'confirm'>('search');
const query = ref('');
const searching = ref(false);
const searched = ref(false);
const searchedFor = ref('');
const searchError = ref('');
const hits = ref<ComicVineVolumeSummary[]>([]);

const selected = ref<ComicVineVolumeSummary | null>(null);
const manualName = ref('');
const fileLibraryId = ref<string>();
const issueStart = ref(1);
const issueEnd = ref<number>();
const adding = ref(false);
const addMessage = ref('');

const runSearch = async () => {
    const q = query.value.trim();
    if (!q) return;
    searching.value = true;
    searched.value = true;
    searchedFor.value = q;
    searchError.value = '';
    hits.value = [];
    try {
        hits.value = await $api('/v2/Comic/Search', { query: { query: q } });
    } catch (e: unknown) {
        const body = typeof e === 'object' && e && 'data' in e ? String((e as { data?: unknown }).data ?? '') : '';
        searchError.value = body || 'Search failed.';
    } finally {
        searching.value = false;
    }
};

const pick = (hit: ComicVineVolumeSummary) => {
    selected.value = hit;
    manualName.value = '';
    issueStart.value = 1;
    issueEnd.value = hit.issueCount || undefined;
    fileLibraryId.value = comicLibraries.value[0]?.key;
    addMessage.value = '';
    step.value = 'confirm';
};

const pickManual = () => {
    selected.value = null;
    manualName.value = query.value.trim();
    issueStart.value = 1;
    issueEnd.value = undefined;
    fileLibraryId.value = comicLibraries.value[0]?.key;
    addMessage.value = '';
    step.value = 'confirm';
};

const backToSearch = () => {
    step.value = 'search';
    addMessage.value = '';
};

const confirmAdd = async () => {
    if (!fileLibraryId.value || (!selected.value && !manualName.value.trim())) return;
    adding.value = true;
    addMessage.value = '';
    try {
        await $api('/v2/Comic', {
            method: 'PUT',
            body: {
                comicVineVolumeId: selected.value?.comicVineVolumeId,
                name: selected.value ? undefined : manualName.value.trim(),
                fileLibraryId: fileLibraryId.value,
                issueStart: Number(issueStart.value),
                issueEnd: issueEnd.value ? Number(issueEnd.value) : undefined,
            },
        });
        step.value = 'search';
        query.value = '';
        hits.value = [];
        searched.value = false;
        selected.value = null;
    } catch (e: unknown) {
        const body = typeof e === 'object' && e && 'data' in e ? String((e as { data?: unknown }).data ?? '') : '';
        addMessage.value = body || 'Could not add comic.';
    } finally {
        adding.value = false;
    }
};
</script>
