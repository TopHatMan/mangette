<template>
    <div class="w-full">
        <div class="flex flex-wrap items-center gap-2 mb-3">
            <p class="text-muted text-sm">{{ data?.totalCount ?? 0 }} chapters</p>
            <UButtonGroup>
                <UButton size="xs" :variant="dlFilter === 'all' ? 'solid' : 'outline'" @click="setDl('all')">All</UButton>
                <UButton size="xs" :variant="dlFilter === 'missing' ? 'solid' : 'outline'" @click="setDl('missing')">Missing</UButton>
                <UButton size="xs" :variant="dlFilter === 'have' ? 'solid' : 'outline'" @click="setDl('have')">Downloaded</UButton>
            </UButtonGroup>
            <UInput v-model="filter.name" size="sm" class="w-40" placeholder="Title" />
            <UPagination
                size="sm"
                :default-page="pagination.pageIndex + 1"
                :items-per-page="pagination.pageSize"
                :total="data?.totalCount ?? 0"
                class="ml-auto"
                @update:page="(p) => (pagination.pageIndex = p - 1)" />
        </div>

        <div class="overflow-x-auto">
            <table class="arr-table">
                <thead>
                    <tr>
                        <th class="w-8"></th>
                        <th>Volume</th>
                        <th>Chapter</th>
                        <th>Title</th>
                        <th>File</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="ch in data?.data ?? []" :id="ch.key" :key="ch.key">
                        <td>
                            <UTooltip :text="ch.downloaded ? 'On disk' : 'Missing'">
                                <UIcon
                                    :name="ch.downloaded ? 'i-lucide-circle-check' : 'i-lucide-circle-dashed'"
                                    :class="ch.downloaded ? 'text-success' : 'text-warning'"
                                    class="size-4" />
                            </UTooltip>
                        </td>
                        <td class="tabular-nums text-muted">{{ ch.volume ?? '—' }}</td>
                        <td class="tabular-nums font-medium">{{ ch.chapterNumber }}</td>
                        <td class="min-w-40">{{ chapterLabel(ch) }}</td>
                        <td class="text-muted text-xs truncate max-w-56" :title="ch.fileName ?? ''">{{ ch.fileName || '—' }}</td>
                        <td class="whitespace-nowrap">
                            <UTooltip text="Interactive search">
                                <UButton
                                    size="xs"
                                    variant="ghost"
                                    icon="i-lucide-list"
                                    :disabled="ch.downloaded"
                                    @click="openInteractive(ch)" />
                            </UTooltip>
                            <UTooltip text="Search this chapter">
                                <UButton
                                    size="xs"
                                    variant="ghost"
                                    icon="i-lucide-search"
                                    :disabled="ch.downloaded"
                                    :loading="grabbing === ch.key"
                                    @click="automaticSearch(ch)" />
                            </UTooltip>
                        </td>
                    </tr>
                </tbody>
            </table>
        </div>

        <UModal v-model:open="searchOpen" title="Interactive Search">
            <template #body>
                <p class="text-muted text-sm mb-3">
                    {{ searchChapter ? `Ch. ${searchChapter.chapterNumber}` : '' }}
                    {{ searchChapter?.title ? ` — ${searchChapter.title}` : '' }}
                </p>
                <p v-if="searchBusy" class="text-muted text-sm">Looking up sites…</p>
                <p v-else-if="!releases.length" class="text-muted text-sm">
                    No sites attached for this chapter. Add a site on the series first.
                </p>
                <div v-else class="flex flex-col gap-2">
                    <div v-for="rel in releases" :key="rel.key" class="flex items-center gap-2 bg-elevated rounded-lg p-2">
                        <div class="grow min-w-0">
                            <p class="font-medium text-sm">{{ rel.connectorName }}</p>
                            <p class="text-muted text-xs truncate">{{ rel.websiteUrl || rel.idOnSite }}</p>
                        </div>
                        <UBadge v-if="rel.preferred" size="sm" color="success" variant="subtle">Preferred</UBadge>
                        <UButton size="xs" :loading="grabbing === rel.key" @click="grab(rel.connectorName)">Download</UButton>
                    </div>
                </div>
            </template>
        </UModal>
    </div>
</template>

<script setup lang="ts">
type Chapter = {
    key: string;
    volume?: number | null;
    chapterNumber: string;
    title?: string | null;
    downloaded: boolean;
    fileName?: string | null;
    mangaConnectorIds?: { key: string; mangaConnectorName: string; useForDownload: boolean }[];
};
type Release = {
    key: string;
    connectorName: string;
    idOnSite: string;
    websiteUrl?: string | null;
    preferred: boolean;
    title?: string | null;
};

const filter = ref<{ name?: string; downloaded?: boolean }>({});
const dlFilter = ref<'all' | 'missing' | 'have'>('all');
const pagination = ref({ pageIndex: 0, pageSize: 50 });

const props = defineProps<{ mangaId: string }>();
const { $api } = useNuxtApp();

const { data, refresh } = useAsyncData(
    FetchKeys.Chapters.Manga(props.mangaId),
    () =>
        $api('/v2/Chapters/Manga/{MangaId}', {
            method: 'POST',
            query: { page: pagination.value.pageIndex + 1, pageSize: pagination.value.pageSize },
            path: { MangaId: props.mangaId },
            body: filter.value,
        }),
    { watch: [pagination, filter], lazy: true, server: false },
);

const setDl = (mode: 'all' | 'missing' | 'have') => {
    dlFilter.value = mode;
    filter.value = {
        ...filter.value,
        downloaded: mode === 'all' ? undefined : mode === 'have',
    };
    pagination.value = { ...pagination.value, pageIndex: 0 };
};

const chapterLabel = (ch: Chapter) => {
    if (ch.title && ch.title !== ch.chapterNumber) return ch.title;
    const vol = ch.volume != null ? `Vol. ${ch.volume} ` : '';
    return `${vol}Ch. ${ch.chapterNumber}`.trim();
};

const searchOpen = ref(false);
const searchBusy = ref(false);
const searchChapter = ref<Chapter | null>(null);
const releases = ref<Release[]>([]);
const grabbing = ref('');

const openInteractive = async (ch: Chapter) => {
    searchChapter.value = ch;
    searchOpen.value = true;
    searchBusy.value = true;
    releases.value = [];
    try {
        releases.value = (await $fetch<Release[]>(`/v2/Chapters/${encodeURIComponent(ch.key)}/Releases`)) ?? [];
    } catch {
        releases.value = [];
    } finally {
        searchBusy.value = false;
    }
};

const grab = async (connectorName?: string) => {
    const id = searchChapter.value?.key;
    if (!id) return;
    grabbing.value = connectorName ?? id;
    try {
        await $fetch(`/v2/Chapters/${encodeURIComponent(id)}/Grab`, {
            method: 'POST',
            body: { connectorName: connectorName ?? null },
        });
        searchOpen.value = false;
        await refresh();
    } finally {
        grabbing.value = '';
    }
};

const automaticSearch = async (ch: Chapter) => {
    grabbing.value = ch.key;
    try {
        await $fetch(`/v2/Chapters/${encodeURIComponent(ch.key)}/Grab`, { method: 'POST', body: {} });
        await refresh();
    } finally {
        grabbing.value = '';
    }
};
</script>
