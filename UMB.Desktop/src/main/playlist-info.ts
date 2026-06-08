import { readFileSync } from 'fs'
import { join } from 'path'

/**
 * Reads vanilla game music metadata straight from the resource dump in Resources/Game.
 *
 * Everything here is implemented in TypeScript (no CLI daemon): the PRC databases
 * (ui_stage_db.prc, ui_bgm_db.prc) are decoded with a small paracobble reader, and
 * song titles come from the msg_bgm MSBT. Display names for series / playlists / stages
 * are the same hard-coded English maps the Sma5h GUI uses (Sma5hMusic.GUI/Helpers/Constants.cs).
 */

// ---------------------------------------------------------------------------
// English display-name maps (ported verbatim from Sma5hMusic.GUI Constants.cs)
// ---------------------------------------------------------------------------

const SERIES_NAMES: Record<string, string> = {
  ui_series_none: 'None',
  ui_series_mario: 'Mario',
  ui_series_mariokart: 'Mario Kart',
  ui_series_wreckingcrew: 'Wrecking Crew',
  ui_series_etc: 'etc',
  ui_series_donkeykong: 'Donkey Kong',
  ui_series_zelda: 'The Legend of Zelda',
  ui_series_metroid: 'Metroid',
  ui_series_yoshi: 'Yoshi',
  ui_series_kirby: 'Kirby',
  ui_series_starfox: 'Starfox',
  ui_series_pokemon: 'Pokémon',
  ui_series_fzero: 'F-Zero',
  ui_series_mother: 'Mother',
  ui_series_fireemblem: 'Fire Emblem',
  ui_series_gamewatch: 'Game & Watch',
  ui_series_palutena: 'Kid Icarus',
  ui_series_wario: 'Wario',
  ui_series_pikmin: 'Pikmin',
  ui_series_famicomrobot: 'Famicon Robot',
  ui_series_doubutsu: 'Animal Crossing',
  ui_series_wiifit: 'Wii Fit',
  ui_series_punchout: 'Punch-Out!!',
  ui_series_xenoblade: 'Xenoblade',
  ui_series_metalgear: 'Metal Gear',
  ui_series_sonic: 'Sonic',
  ui_series_rockman: 'Megaman',
  ui_series_pacman: 'Pacman',
  ui_series_streetfighter: 'Street Fighter',
  ui_series_finalfantasy: 'Final Fantasy',
  ui_series_bayonetta: 'Bayonetta',
  ui_series_splatoon: 'Splatoon',
  ui_series_castlevania: 'Castlevania',
  ui_series_smashbros: 'Smash Bros',
  ui_series_arms: 'Arms',
  ui_series_persona: 'Persona',
  ui_series_dragonquest: 'Dragon Quest',
  ui_series_banjokazooie: 'Banjo-Kazooie',
  ui_series_fatalfury: 'Fatal Fury',
  ui_series_minecraft: 'Minecraft',
  ui_series_tekken: 'Tekken',
  ui_series_kingdomhearts: 'Kingdom Hearts'
}

const PLAYLIST_NAMES: Record<string, string> = {
  bgmsmashbtl: 'Smash Battle',
  bgmsmashmenu: 'Smash Menu',
  bgmsmashmode: 'Smash Mode',
  bgmstageedit: 'Stage Edit',
  bgmboss: 'Boss',
  bgmadventure: 'Adventure',
  bgmmario: 'Mario',
  bgmmkart: 'Mario Kart',
  bgmdk: 'Donkey Kong',
  bgmkirby: 'Kirby',
  bgmzelda: 'The Legend of Zelda',
  bgmmetroid: 'Metroid',
  bgmfzero: 'F-Zero',
  bgmyoshi: 'Yoshi',
  bgmfox: 'Starfox',
  bgmpokemon: 'Pokémon',
  bgmmother: 'Mother',
  bgmfe: 'Fire Emblem',
  bgmgamewatch: 'Game & Watch',
  bgmicaros: 'Kid Icarus',
  bgmwario: 'Wario',
  bgmpikmin: 'Pikmin',
  bgmanimal: 'Animal Crossing',
  bgmwiifit: 'Wii-Fit',
  bgmpunchout: 'Punch-Out!!',
  bgmxenoblade: 'Xenoblade',
  bgmspla: 'Splatoon',
  bgmmetalgear: 'Metal Gear',
  bgmsonic: 'Sonic',
  bgmrockman: 'Megaman',
  bgmpacman: 'Pacman',
  bgmsf: 'Street Fighter',
  bgmff: 'Final Fantasy',
  bgmbeyo: 'Bayonetta',
  bgmdracula: 'Castlevania',
  bgmother: 'Other',
  bgmjack: 'Persona',
  bgmbrave: 'Dragon Quest',
  bgmbuddy: 'Banjo-Kazooie',
  bgmdolly: 'Fatal Fury',
  bgmmaster: 'Fire Emblem Three Houses',
  bgmtantan: 'Arms',
  bgmpickel: 'Minecraft',
  bgmedge: 'Final Fantasy (Sephiroth)',
  bgmelement: 'Xenoblade 2 (Pyra & Mythra)',
  bgmdemon: 'Tekken',
  bgmtrail: 'Kingdom Hearts',
  bgmplaylist: 'Playlist'
}

const STAGE_NAMES: Record<string, string> = {
  ui_stage_random: '(H) Random',
  ui_stage_random_normal: '(H) Random Normal',
  ui_stage_random_battle_field: '(H) Random Battlefield',
  ui_stage_random_end: '(H) Random Ω Form',
  ui_stage_battle_field: 'Battlefield',
  ui_stage_battle_field_l: 'Big Battlefield',
  ui_stage_end: 'Final Destination',
  ui_stage_mario_castle64: "Peach's Castle",
  ui_stage_dk_jungle: 'Kongo Jungle',
  ui_stage_zelda_hyrule: 'Hyrule Castle',
  ui_stage_yoshi_story: 'Super Happy Tree',
  ui_stage_kirby_pupupu64: 'Dream Land',
  ui_stage_poke_yamabuki: 'Saffron City',
  ui_stage_mario_past64: 'Mushroom Kingdom',
  ui_stage_mario_castledx: "Princess Peach's Castle",
  ui_stage_mario_rainbow: 'Rainbow Cruise',
  ui_stage_dk_waterfall: 'Kongo Falls',
  ui_stage_dk_lodge: 'Jungle Japes',
  ui_stage_zelda_greatbay: 'Great Bay',
  ui_stage_zelda_temple: 'Temple',
  ui_stage_metroid_zebesdx: 'Brinstar',
  ui_stage_yoshi_yoster: "Yoshi's Island (Melee)",
  ui_stage_yoshi_cartboard: "Yoshi's Story",
  ui_stage_kirby_fountain: 'Fountain of Dreams',
  ui_stage_kirby_greens: 'Greens Greens',
  ui_stage_fox_corneria: 'Corneria',
  ui_stage_fox_venom: 'Venom',
  ui_stage_poke_stadium: 'Pokémon Stadium',
  ui_stage_mother_onett: 'Onett',
  ui_stage_mario_pastusa: 'Mushroom Kingdom II',
  ui_stage_metroid_kraid: 'Brinstar Depths',
  ui_stage_fzero_bigblue: 'Big Blue',
  ui_stage_mother_fourside: 'Fourside',
  ui_stage_mario_dolpic: 'Delfino Plaza',
  ui_stage_mario_pastx: 'Mushroomy Kingdom',
  ui_stage_kart_circuitx: 'Figure-8 Circuit',
  ui_stage_wario_madein: 'WarioWare, Inc',
  ui_stage_zelda_oldin: 'Bridge of Eldin',
  ui_stage_metroid_norfair: 'Norfair',
  ui_stage_metroid_orpheon: 'Frigate Orpheon',
  ui_stage_yoshi_island: "Yoshi's Island",
  ui_stage_kirby_halberd: 'Halberd',
  ui_stage_fox_lylatcruise: 'Lylat Cruise',
  ui_stage_poke_stadium2: 'Pokémon Stadium 2',
  ui_stage_fzero_porttown: 'Port Town Aero Dive',
  ui_stage_fe_siege: 'Castle Siege',
  ui_stage_pikmin_planet: 'Distant Planet',
  ui_stage_animal_village: 'Smashville',
  ui_stage_mother_newpork: 'New Pork City',
  ui_stage_ice_top: 'Summit',
  ui_stage_icarus_skyworld: 'Skyworld',
  ui_stage_mg_shadowmoses: 'Shadow Moses Island',
  ui_stage_luigimansion: "Luigi's Mansion",
  ui_stage_zelda_pirates: 'Pirate Ship',
  ui_stage_poke_tengam: 'Spear Pillar',
  ui_stage_75m: '75 m',
  ui_stage_mariobros: 'Mario Bros.',
  ui_stage_plankton: 'Hanenbow',
  ui_stage_sonic_greenhill: 'Green Hill Zone',
  ui_stage_mario_3dland: '3D Land',
  ui_stage_mario_newbros2: 'Golden Plains',
  ui_stage_mario_paper: 'Paper Mario',
  ui_stage_zelda_gerudo: 'Gerudo Valley',
  ui_stage_zelda_train: 'Spirit Train',
  ui_stage_kirby_gameboy: 'Dream Land GB',
  ui_stage_poke_unova: 'Unova Pokémon League',
  ui_stage_poke_tower: 'Prism Tower',
  ui_stage_fzero_mutecity3ds: 'Mute City SNES',
  ui_stage_mother_magicant: 'Magicant',
  ui_stage_fe_arena: 'Arena Ferox',
  ui_stage_icarus_uprising: 'Reset Bomb Forest',
  ui_stage_animal_island: 'Tortimer Island',
  ui_stage_balloonfight: 'Balloon Fight',
  ui_stage_nintendogs: 'Living Room',
  ui_stage_streetpass: 'Find Mii',
  ui_stage_tomodachi: 'Tomodachi Life',
  ui_stage_pictochat2: 'PictoChat 2',
  ui_stage_mario_uworld: 'Mushroom Kingdom U',
  ui_stage_mario_galaxy: 'Mario Galaxy',
  ui_stage_kart_circuitfor: 'Mario Circuit',
  ui_stage_zelda_skyward: 'Skyloft',
  ui_stage_kirby_cave: 'The Great Cave Offensive',
  ui_stage_poke_kalos: 'Kalos Pokémon League',
  ui_stage_fe_colloseum: 'Coliseum',
  ui_stage_flatzonex: 'Flat Zone X',
  ui_stage_icarus_angeland: "Palutena's Temple",
  ui_stage_wario_gamer: 'Gamer',
  ui_stage_pikmin_garden: 'Garden of Hope',
  ui_stage_animal_city: 'Town and City',
  ui_stage_wiifit: 'Wii Fit Studio',
  ui_stage_punchoutsb: 'Boxing Ring',
  ui_stage_xeno_gaur: 'Gaur Plain',
  ui_stage_duckhunt: 'Duck Hunt',
  ui_stage_wreckingcrew: 'Wrecking Crew',
  ui_stage_pilotwings: 'Pilotwings',
  ui_stage_wufuisland: 'Wuhu Island',
  ui_stage_sonic_windyhill: 'Windy Hill Zone',
  ui_stage_rock_wily: 'Wily Castle',
  ui_stage_pac_land: 'PAC-LAND',
  ui_stage_mario_maker: 'Super Mario Maker',
  ui_stage_sf_suzaku: 'Suzaku Castle',
  ui_stage_ff_midgar: 'Midgar',
  ui_stage_bayo_clock: 'Umbra Clock Tower',
  ui_stage_mario_odyssey: 'New Donk City Hall',
  ui_stage_zelda_tower: 'Great Plateau Tower',
  ui_stage_spla_parking: 'Moray Towers',
  ui_stage_dracula_castle: "Dracula's Castle",
  ui_stage_bonus_game: '(H) Bonus Game',
  ui_stage_training: '(H) Stage Training',
  ui_stage_general_all: '(H) General All',
  ui_stage_setting_stage: '(H) Setting Stage',
  ui_stage_sham_fight: '(H) Sham Fight',
  ui_stage_campaign_map: '(H) Campaign Map',
  ui_stage_menu_music: '(H) Menu Music',
  ui_stage_boss_ganon: '(H) Boss Ganon',
  ui_stage_boss_rathalos: '(H) Boss Rathalos',
  ui_stage_boss_marx: '(H) Boss Marx',
  ui_stage_boss_dracula: '(H) Boss Dracula',
  ui_stage_boss_galleom: '(H) Boss Galleom',
  ui_stage_boss_final: '(H) Boss Final',
  ui_stage_boss_final2: '(H) Boss Final 2',
  ui_stage_boss_final3: '(H) Boss Final 3',
  ui_stage_punchoutw: '(H) Boxing Ring',
  ui_stage_edit: '(H) Stage Edit',
  ui_stage_homerun: '(H) Home Run',
  ui_stage_jack_mementoes: 'Mementos',
  ui_stage_brave_altar: "Yggdrasil's Altar",
  ui_stage_buddy_spiral: 'Spiral Mountain',
  ui_stage_dolly_stadium: 'King of Fighter Stadium',
  ui_stage_fe_shrine: 'Garreg Mach Monastery',
  ui_stage_tantan_spring: 'Spring Stadium',
  ui_stage_pickel_world: 'Minecraft World',
  ui_stage_ff_cave: 'Northern Cave',
  ui_stage_xeno_alst: 'Cloud Sea of Alrest',
  ui_stage_demon_dojo: 'Mishima Dojo',
  ui_stage_trail_castle: 'Hollow Bastion',
  ui_stage_battle_field_s: 'Small Battlefield'
}

// ---------------------------------------------------------------------------
// ParamLabels (hash40 -> label string), parsed once and cached
// ---------------------------------------------------------------------------

let labelCache: Map<bigint, string> | null = null

function loadLabels(workspace: string): Map<bigint, string> {
  if (labelCache) return labelCache
  const csv = readFileSync(join(workspace, 'Resources', 'ParamLabels.csv'), 'utf-8')
  const map = new Map<bigint, string>()
  for (const line of csv.split('\n')) {
    const trimmed = line.replace(/\r$/, '')
    if (!trimmed) continue
    const comma = trimmed.indexOf(',')
    if (comma < 0) continue
    const hash = BigInt(trimmed.slice(0, comma))
    map.set(hash, trimmed.slice(comma + 1))
  }
  labelCache = map
  return map
}

// ---------------------------------------------------------------------------
// PRC (paracobble) reader
// ---------------------------------------------------------------------------

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type PrcNode = any

function readPrc(path: string, labels: Map<bigint, string>): Record<string, PrcNode> {
  const buf = readFileSync(path)
  if (buf.toString('latin1', 0, 8) !== 'paracobn') {
    throw new Error(`Not a PRC file: ${path}`)
  }
  const hashTableSize = buf.readUInt32LE(8)
  const refTableSize = buf.readUInt32LE(12)
  const hashCount = hashTableSize / 8
  const hashes: bigint[] = new Array(hashCount)
  for (let i = 0; i < hashCount; i++) {
    hashes[i] = buf.readBigUInt64LE(16 + i * 8)
  }
  const refStart = 16 + hashTableSize
  const paramStart = 16 + hashTableSize + refTableSize

  const hstr = (h: bigint): string => labels.get(h) ?? '0x' + h.toString(16)

  function readParam(pos: number): PrcNode {
    const type = buf[pos]
    const p = pos + 1
    switch (type) {
      case 12: {
        // struct
        const size = buf.readInt32LE(p)
        const refOffset = buf.readInt32LE(p + 4)
        const obj: Record<string, PrcNode> = {}
        for (let i = 0; i < size; i++) {
          const entry = refStart + refOffset + i * 8
          const hashIndex = buf.readInt32LE(entry)
          const paramOffset = buf.readInt32LE(entry + 4)
          obj[hstr(hashes[hashIndex])] = readParam(pos + paramOffset)
        }
        return obj
      }
      case 11: {
        // list
        const count = buf.readInt32LE(p)
        const arr: PrcNode[] = new Array(count)
        for (let i = 0; i < count; i++) {
          arr[i] = readParam(pos + buf.readInt32LE(p + 4 + i * 4))
        }
        return arr
      }
      case 1:
        return buf[p] !== 0
      case 2:
        return buf.readInt8(p)
      case 3:
        return buf[p]
      case 4:
        return buf.readInt16LE(p)
      case 5:
        return buf.readUInt16LE(p)
      case 6:
        return buf.readInt32LE(p)
      case 7:
        return buf.readUInt32LE(p)
      case 8:
        return buf.readFloatLE(p)
      case 9: {
        // hash40 stored as a uint32 index into the hash table
        const idx = buf.readUInt32LE(p)
        return hstr(hashes[idx])
      }
      case 10: {
        // string stored as a uint32 offset into the ref table (null terminated)
        const off = refStart + buf.readInt32LE(p)
        let end = off
        while (buf[end] !== 0) end++
        return buf.toString('utf-8', off, end)
      }
      default:
        throw new Error(`Unknown PRC param type ${type} at offset ${pos}`)
    }
  }

  return readParam(paramStart) as Record<string, PrcNode>
}

// ---------------------------------------------------------------------------
// MSBT reader (LBL1 labels + TXT2 UTF-16LE strings)
// ---------------------------------------------------------------------------

function readMsbt(path: string): Record<string, string> {
  const buf = readFileSync(path)
  if (buf.toString('latin1', 0, 8) !== 'MsgStdBn') {
    throw new Error(`Not an MSBT file: ${path}`)
  }
  const sectionCount = buf.readUInt16LE(0x0e)
  const labelByIndex = new Map<number, string>()
  const strings: string[] = []

  let pos = 0x20
  for (let s = 0; s < sectionCount; s++) {
    const magic = buf.toString('latin1', pos, pos + 4)
    const size = buf.readUInt32LE(pos + 4)
    const body = pos + 0x10
    if (magic === 'LBL1') {
      const groupCount = buf.readUInt32LE(body)
      for (let g = 0; g < groupCount; g++) {
        const count = buf.readUInt32LE(body + 4 + g * 8)
        const offset = buf.readUInt32LE(body + 8 + g * 8)
        let p = body + offset
        for (let c = 0; c < count; c++) {
          const len = buf[p]
          p += 1
          const name = buf.toString('latin1', p, p + len)
          p += len
          const index = buf.readUInt32LE(p)
          p += 4
          labelByIndex.set(index, name)
        }
      }
    } else if (magic === 'TXT2') {
      const count = buf.readUInt32LE(body)
      for (let i = 0; i < count; i++) {
        const start = body + buf.readUInt32LE(body + 4 + i * 4)
        const end = i + 1 < count ? body + buf.readUInt32LE(body + 4 + (i + 1) * 4) : body + size
        strings.push(buf.toString('utf16le', start, end).split('\u0000')[0])
      }
    }
    pos = body + size
    pos = (pos + 15) & ~15 // sections are 16-byte aligned
  }

  const out: Record<string, string> = {}
  for (const [index, name] of labelByIndex) {
    if (index < strings.length) out[name] = strings[index]
  }
  return out
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

export interface PlaylistInfo {
  id: string
  name: string
  series: string[]
  songCount: number
}

export interface StageSong {
  order: number
  bgmId: string
  name: string
}

export interface StageInfo {
  uiStageId: string
  name: string
  hidden: boolean
  seriesId: string
  seriesName: string
  playlistId: string
  playlistName: string
  order: number
  songs: StageSong[]
}

export interface PlaylistInfoData {
  playlists: PlaylistInfo[]
  stages: StageInfo[]
}

let dataCache: { workspace: string; data: PlaylistInfoData } | null = null
let bgmTitleCache: { workspace: string; map: Map<string, string> } | null = null

/**
 * Maps every vanilla ui_bgm_id to its localised English title (msg_bgm `bgm_title_<name_id>`).
 * Cached per workspace. Throws if the game resource dump is absent — callers should fall back.
 */
export function getVanillaBgmTitles(workspace: string): Map<string, string> {
  if (bgmTitleCache && bgmTitleCache.workspace === workspace) return bgmTitleCache.map

  const dbDir = join(workspace, 'Resources', 'Game', 'ui', 'param', 'database')
  const labels = loadLabels(workspace)
  const bgmDb = readPrc(join(dbDir, 'ui_bgm_db.prc'), labels)
  const msbt = readMsbt(join(workspace, 'Resources', 'Game', 'ui', 'message', 'msg_bgm+us_en.msbt'))

  const map = new Map<string, string>()
  for (const entry of (bgmDb['db_root'] as PrcNode[]) ?? []) {
    const id = entry['ui_bgm_id'] as string
    const title = msbt['bgm_title_' + entry['name_id']]
    if (id) map.set(id, title || id)
  }

  bgmTitleCache = { workspace, map }
  return map
}

export interface VanillaGameTitle {
  id: string // bare id (ui_gametitle_ prefix stripped) — matches series.toml [[games]].id
  name: string
  seriesId: string // ui_series_*
}

let gameTitleCache: { workspace: string; list: VanillaGameTitle[] } | null = null

/** Lists every vanilla game title (id, localised name, series). Throws if resources absent. */
export function getVanillaGameTitles(workspace: string): VanillaGameTitle[] {
  if (gameTitleCache && gameTitleCache.workspace === workspace) return gameTitleCache.list

  const dbDir = join(workspace, 'Resources', 'Game', 'ui', 'param', 'database')
  const labels = loadLabels(workspace)
  const gtDb = readPrc(join(dbDir, 'ui_gametitle_db.prc'), labels)
  const msbt = readMsbt(join(workspace, 'Resources', 'Game', 'ui', 'message', 'msg_title+us_en.msbt'))

  const list: VanillaGameTitle[] = []
  for (const entry of (gtDb['db_root'] as PrcNode[]) ?? []) {
    const gameTitleId = entry['ui_gametitle_id'] as string
    if (!gameTitleId) continue
    const id = gameTitleId.replace(/^ui_gametitle_/, '')
    const name = msbt['tit_' + entry['name_id']] || id
    list.push({ id, name, seriesId: (entry['ui_series_id'] as string) ?? '' })
  }

  gameTitleCache = { workspace, list }
  return list
}

export interface VanillaSong {
  bgmId: string
  infoId: string // info0 of the song's stream set — what info1 references for a pinch link
  name: string
  seriesId: string // ui_series_* (via the song's game title)
}

let vanillaSongCache: { workspace: string; list: VanillaSong[] } | null = null

/** Lists every vanilla song with its info id + series. Throws if resources absent. */
export function getVanillaSongs(workspace: string): VanillaSong[] {
  if (vanillaSongCache && vanillaSongCache.workspace === workspace) return vanillaSongCache.list

  const dbDir = join(workspace, 'Resources', 'Game', 'ui', 'param', 'database')
  const labels = loadLabels(workspace)
  const bgmDb = readPrc(join(dbDir, 'ui_bgm_db.prc'), labels)
  const gtDb = readPrc(join(dbDir, 'ui_gametitle_db.prc'), labels)
  const msbt = readMsbt(join(workspace, 'Resources', 'Game', 'ui', 'message', 'msg_bgm+us_en.msbt'))

  const gameToSeries = new Map<string, string>()
  for (const entry of (gtDb['db_root'] as PrcNode[]) ?? []) {
    const gtId = entry['ui_gametitle_id'] as string
    if (gtId) gameToSeries.set(gtId, (entry['ui_series_id'] as string) ?? '')
  }

  const info0ByStreamSet = new Map<string, string>()
  for (const set of (bgmDb['stream_set'] as PrcNode[]) ?? []) {
    const setId = set['stream_set_id'] as string
    const info0 = set['info0'] as string
    if (setId && info0) info0ByStreamSet.set(setId, info0)
  }

  const list: VanillaSong[] = []
  for (const entry of (bgmDb['db_root'] as PrcNode[]) ?? []) {
    const bgmId = entry['ui_bgm_id'] as string
    if (!bgmId) continue
    const infoId = info0ByStreamSet.get(entry['stream_set_id'] as string)
    if (!infoId || !infoId.startsWith('info_')) continue
    const name = msbt['bgm_title_' + entry['name_id']] || bgmId
    list.push({ bgmId, infoId, name, seriesId: gameToSeries.get(entry['ui_gametitle_id'] as string) ?? '' })
  }

  vanillaSongCache = { workspace, list }
  return list
}

export function getPlaylistInfo(workspace: string): PlaylistInfoData {
  if (dataCache && dataCache.workspace === workspace) return dataCache.data

  const dbDir = join(workspace, 'Resources', 'Game', 'ui', 'param', 'database')
  const labels = loadLabels(workspace)
  const stageDb = readPrc(join(dbDir, 'ui_stage_db.prc'), labels)
  const bgmDb = readPrc(join(dbDir, 'ui_bgm_db.prc'), labels)
  const msbt = readMsbt(join(workspace, 'Resources', 'Game', 'ui', 'message', 'msg_bgm+us_en.msbt'))

  // ui_bgm_id -> English title (msg_bgm label is "bgm_title_" + name_id)
  const bgmTitle = new Map<string, string>()
  for (const entry of (bgmDb['db_root'] as PrcNode[]) ?? []) {
    const id = entry['ui_bgm_id'] as string
    const title = msbt['bgm_title_' + entry['name_id']]
    if (id) bgmTitle.set(id, title || id)
  }

  // playlist members are the root entries named bgm* that hold a list of tracks
  const playlistIds = Object.keys(bgmDb).filter((k) => k.startsWith('bgm') && Array.isArray(bgmDb[k]))

  const stagesRaw = (stageDb['db_root'] as PrcNode[]) ?? []

  // playlist -> set of series that reference it (derived from the stages using it)
  const playlistToSeries = new Map<string, Set<string>>()
  for (const stage of stagesRaw) {
    const playlistId = stage['bgm_set_id'] as string
    if (!playlistId) continue
    if (!playlistToSeries.has(playlistId)) playlistToSeries.set(playlistId, new Set())
    const seriesId = stage['ui_series_id'] as string
    if (seriesId) playlistToSeries.get(playlistId)!.add(seriesId)
  }

  const playlists: PlaylistInfo[] = playlistIds
    .map((id) => {
      const tracks = bgmDb[id] as PrcNode[]
      const series = [...(playlistToSeries.get(id) ?? [])]
        .map((sid) => SERIES_NAMES[sid] ?? sid)
        .filter((name) => name && name !== 'None')
        .sort((a, b) => a.localeCompare(b))
      return {
        id,
        name: PLAYLIST_NAMES[id] ?? id,
        series,
        songCount: tracks.length
      }
    })
    .sort((a, b) => a.name.localeCompare(b.name))

  const stages: StageInfo[] = stagesRaw
    .map((stage) => {
      const uiStageId = stage['ui_stage_id'] as string
      const name = STAGE_NAMES[uiStageId] ?? uiStageId
      const seriesId = (stage['ui_series_id'] as string) ?? ''
      const playlistId = (stage['bgm_set_id'] as string) ?? ''
      const order = (stage['bgm_setting_no'] as number) ?? 0
      const tracks = (bgmDb[playlistId] as PrcNode[]) ?? []
      const songs: StageSong[] = tracks
        .filter((track) => ((track['incidence' + order] as number) ?? 0) > 0)
        .map((track) => {
          const bgmId = track['ui_bgm_id'] as string
          return {
            order: track['order' + order] as number,
            bgmId,
            name: bgmTitle.get(bgmId) ?? bgmId
          }
        })
        .sort((a, b) => a.order - b.order)
      return {
        uiStageId,
        name,
        hidden: name.startsWith('(H)'),
        seriesId,
        seriesName: SERIES_NAMES[seriesId] ?? seriesId,
        playlistId,
        playlistName: PLAYLIST_NAMES[playlistId] ?? playlistId,
        order,
        songs
      }
    })
    .sort(
      (a, b) => Number(a.hidden) - Number(b.hidden) || a.name.localeCompare(b.name)
    )

  const data: PlaylistInfoData = { playlists, stages }
  dataCache = { workspace, data }
  return data
}
