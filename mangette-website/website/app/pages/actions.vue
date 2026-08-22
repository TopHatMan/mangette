<template>
    <div class="arr-page">
        <div class="arr-page__head">
            <div>
                <h1 class="arr-page__title">Activity</h1>
                <p class="arr-page__hint">
                    {{ data?.totalCount ?? 0 }}
                    {{ params.action === 'ChapterDownloaded' ? 'successful downloads' : 'actions' }}
                </p>
            </div>
            <div class="flex flex-wrap items-center gap-2">
                <USelect v-model="params.action" :items="typeItems" class="w-44" @change="refreshData" />
                <UInput v-model="params.start" type="datetime-local" class="w-52" @change="refreshData" />
                <UInput v-model="params.end" type="datetime-local" class="w-52" @change="refreshData" />
                <UTooltip text="No time limit">
                    <UButton color="neutral" variant="outline" icon="i-lucide-infinity" @click="noTimeLimit" />
                </UTooltip>
                <UButton color="neutral" variant="outline" icon="i-lucide-rotate-ccw" @click="resetFilter">Reset</UButton>
                <UButton variant="soft" icon="i-lucide-refresh-ccw" :loading="status === 'pending'" @click="refreshData" />
            </div>
        </div>

        <p v-if="status !== 'pending' && !(data?.data ?? []).length" class="text-muted text-sm">
            No downloads in this range.
        </p>

        <div v-else class="overflow-x-auto">
            <table class="arr-table">
                <thead>
                    <tr>
                        <th></th>
                        <th>Date</th>
                        <th>Event</th>
                        <th>Series</th>
                        <th>Chapter</th>
                        <th>File</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="row in data?.data ?? []" :key="row.key">
                        <td class="w-12">
                            <NuxtLink v-if="row.mangaId" :to="seriesLink(row)">
                                <img :src="coverSrc(row.mangaId)" alt="" class="w-8 h-11 object-cover rounded" />
                            </NuxtLink>
                        </td>
                        <td class="text-sm whitespace-nowrap tabular-nums">{{ formatStamp(row.performedAt) }}</td>
                        <td>
                            <UBadge :color="eventColor(row.action)" variant="subtle" size="sm">{{ eventLabel(row.action) }}</UBadge>
                        </td>
                        <td>
                            <NuxtLink v-if="row.mangaId" :to="seriesLink(row)" class="font-medium hover:underline">
                                {{ row.mangaName || 'Unknown series' }}
                            </NuxtLink>
                            <span v-else class="text-muted">—</span>
                        </td>
                        <td>
                            <NuxtLink v-if="row.chapterId && row.mangaId" :to="chapterLink(row)" class="hover:underline">
                                {{ chapterLabel(row) }}
                            </NuxtLink>
                            <span v-else class="text-muted">{{ chapterLabel(row) || '—' }}</span>
                        </td>
                        <td class="text-muted text-sm max-w-xs truncate" :title="row.filename || ''">{{ row.filename || '—' }}</td>
                    </tr>
                </tbody>
            </table>
        </div>

        <div class="flex justify-center mt-4">
            <UPagination
                :default-page="pagination.pageIndex + 1"
                :items-per-page="pagination.pageSize"
                :total="data?.totalCount ?? 0"
                @update:page="(p) => (pagination.pageIndex = p - 1)" />
        </div>
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type ActionsFilterRecord = components['schemas']['ActionsFilterRecord'];
type ApiAction = components['schemas']['ActionRecord'];
type HistoryRow = ApiAction & {
    mangaName?: string | null;
    chapterNumber?: string | null;
    volumeNumber?: number | null;
    chapterTitle?: string | null;
    filename?: string | null;
};

const { $api } = useNuxtApp();

const pagination = ref({ pageIndex: 0, pageSize: 25 });

const toLocalInput = (ms: number) => {
    const d = new Date(ms - new Date().getTimezoneOffset() * 60 * 1000);
    return d.toISOString().slice(0, 16);
};

const formatStamp = (value: string) => {
    if (!value) return '';
    const iso = /Z$|[+-]\d{2}:\d{2}$/.test(value) ? value : `${value}Z`;
    return new Date(iso).toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' });
};

const toUtcIso = (localInput: string, endOfMinute = false) => {
    const ms = new Date(localInput).getTime() + (endOfMinute ? 60 * 1000 - 1 : 0);
    return new Date(ms).toISOString();
};

const params = ref<Partial<ActionsFilterRecord>>({
    ...useRoute().query,
    action: 'ChapterDownloaded',
    start: toLocalInput(Date.now() - 24 * 60 * 60 * 1000),
    end: toLocalInput(Date.now()),
});

const { data, refresh, status } = useAsyncData(
    FetchKeys.Actions.Page(params.value, pagination.value.pageIndex),
    () =>
        $api('/v2/Actions/Filter', {
            method: 'POST',
            body: {
                ...params.value,
                start: params.value.start ? toUtcIso(String(params.value.start)) : null,
                end: params.value.end ? toUtcIso(String(params.value.end), true) : null,
            },
            query: { page: pagination.value.pageIndex + 1, pageSize: pagination.value.pageSize },
        }),
    { watch: [pagination.value], lazy: true, server: false },
);

const { data: ActionTypes } = useApi('/v2/Actions/Types', { key: FetchKeys.Actions.Types, server: false });

const typeItems = computed(() =>
    (ActionTypes.value ?? []).map((t: string) => ({
        label: t === 'ChapterDownloaded' ? 'Downloads' : t.replace(/([A-Z])/g, ' $1').trim(),
        value: t,
    })),
);

const eventLabel = (action: string) => {
    if (action === 'ChapterDownloaded') return 'Downloaded';
    if (action === 'ChaptersRetrieved') return 'Updated';
    if (action === 'CoverDownloaded') return 'Cover';
    return action.replace(/([A-Z])/g, ' $1').trim();
};

const eventColor = (action: string) => (action === 'ChapterDownloaded' ? 'success' : 'neutral');

const coverSrc = (mangaId: string) => `/v2/Manga/${mangaId}/Cover/Small`;
const seriesLink = (row: HistoryRow) => `/manga/${row.mangaId}?return=${encodeURIComponent('/actions')}`;
const chapterLink = (row: HistoryRow) => `${seriesLink(row)}#${row.chapterId}`;

const chapterLabel = (row: HistoryRow) => {
    if (!row.chapterNumber) return '';
    const vol = row.volumeNumber != null ? `Vol. ${row.volumeNumber} ` : '';
    const title = row.chapterTitle ? ` – ${row.chapterTitle}` : '';
    return `${vol}Ch. ${row.chapterNumber}${title}`;
};

const resetFilter = async () => {
    params.value = {
        ...useRoute().query,
        action: 'ChapterDownloaded',
        start: toLocalInput(Date.now() - 24 * 60 * 60 * 1000),
        end: toLocalInput(Date.now()),
    };
    await refreshData();
};

const noTimeLimit = async () => {
    params.value = {
        ...params.value,
        start: toLocalInput(0),
        end: toLocalInput(Date.now()),
    };
    await refreshData();
};

const refreshData = async (): Promise<void> => {
    if (!params.value.start || !params.value.end) return Promise.reject();
    pagination.value.pageIndex = 0;
    await refresh();
};

defineShortcuts({ meta_r: { usingInput: true, handler: refreshData } });
useHead({ title: 'Activity' });
</script>
