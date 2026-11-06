#ifndef PATHWEFT_OFFICE_H
#define PATHWEFT_OFFICE_H
#include <stddef.h>
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif

/* x64 C ABI v1. All calls for a document are serial. Input bytes are borrowed
   only during open. Close releases the imported model; the caller owns the
   containing process and must not unload a DLL with donor threads still alive. */
typedef struct PwOfficeFrame {
    uint32_t width, height, format; /* 0: premultiplied RGBA8; 1: in-memory PDF */
    size_t length;
    const uint8_t *pixels;
} PwOfficeFrame;

uint32_t pw_office_abi(void);
const char *pw_office_error(void); /* thread-local UTF-8, copy before next error */
void *pw_office_open(const uint8_t *bytes, size_t length,
                     const uint8_t *extension, size_t extension_length);
const char *pw_office_info(void *document); /* JSON, valid until close */
int32_t pw_office_render(void *document, uint32_t part, uint32_t page, uint32_t width);
const PwOfficeFrame *pw_office_frame(void *document); /* copy before render/close */

/* Additive extraction extension: start at unit=offset=0; limit=1024..49152
   serialized UTF-8 bytes. Follow JSON's next {unit,offset} until null. Records
   contain location, UTF-8 offset within that unit and text. Coverage describes
   imported-model limits; empty text is not proof of image/OCR content.
   Return: JSON owned until next extract/close, or NULL with pw_office_error.
   Extract preserves the frame, and render preserves extraction JSON. */
const char *pw_office_extract(void *document, uint64_t unit, size_t offset, size_t limit);
void pw_office_close(void *document);

#ifdef __cplusplus
}
#endif
#endif
