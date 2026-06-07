/**
 * Protocolo de fio SelfDesk — constantes autoritativas (seção 5 do SPEC).
 * Todos os campos multibyte são big-endian.
 * Espelhar alterações em Protocol.cs.
 */

export const PROTOCOL_VERSION = 0x01;

export const MessageType = {
  HELLO:       0x01,
  AUTH:        0x02,
  AUTH_OK:     0x03,
  AUTH_FAIL:   0x04,
  CHALLENGE:   0x05,
  VIDEO_FRAME:     0x10,
  REQUEST_IDR:     0x11,
  INPUT_EVENT:     0x20,
  SENDER_UP:   0x30,
  SENDER_DOWN: 0x31,
  PING:        0x40,
  PONG:        0x41,
  BYE:         0x50,
  FILE_HEADER: 0x60,
  CLIPBOARD:   0x61,
  FILE_CHUNK:  0x62,
  FILE_DONE:   0x63,
  FILE_ERROR:      0x64,
  MONITOR_LIST:    0x70,
  SELECT_MONITOR:  0x71,
  AUDIO_FRAME:     0x80,
  REMOTE_REBOOT:   0x90,
} as const;

export type MessageTypeValue = (typeof MessageType)[keyof typeof MessageType];

/** Tamanho fixo do envelope (cabeçalho) em bytes. */
export const HEADER_SIZE    = 22;
/** Offset e tamanho do campo PEER_ID dentro do envelope. */
export const PEER_ID_OFFSET = 2;
export const PEER_ID_SIZE   = 16;
/** Offset do campo LENGTH (uint32 big-endian). */
export const LENGTH_OFFSET  = 18;

/** Tamanho do nonce de desafio em bytes. */
export const NONCE_SIZE = 32;

/** Intervalos de heartbeat em ms. */
export const PING_INTERVAL_MS = 5_000;
export const PONG_TIMEOUT_MS  = 15_000;

export type Role = 'sender' | 'receiver';

/** Payload de VIDEO_FRAME — offsets dentro do payload (não do envelope). */
export const VideoFrame = {
  TIMESTAMP_OFFSET: 0,  // uint64 8B
  WIDTH_OFFSET:     8,  // uint16 2B
  HEIGHT_OFFSET:    10, // uint16 2B
  CODEC_OFFSET:     12, // uint8  1B
  FLAGS_OFFSET:     13, // uint8  1B
  DATA_OFFSET:      14, // início dos dados codificados
  CODEC_JPEG:       0x01,
  CODEC_H264:       0x02,
  FLAG_KEYFRAME:    0x01,
} as const;

/** Payload de INPUT_EVENT — primeiro byte = KIND. */
export const InputEvent = {
  MOUSE_MOVE:     0x01,
  MOUSE_BUTTON:   0x02,
  MOUSE_WHEEL:    0x03,
  MOUSE_MOVE_REL: 0x04,
  KEY:            0x10,
  STATE_UP:     0,
  STATE_DOWN:   1,
  BTN_LEFT:     0,
  BTN_RIGHT:    1,
  BTN_MIDDLE:   2,
  BTN_X1:       3,
  BTN_X2:       4,
  MOD_SHIFT:    0x01,
  MOD_CTRL:     0x02,
  MOD_ALT:      0x04,
  MOD_WIN:      0x08,
} as const;
