def get_boilerplate_manifest():
    return {
        "type": "Manifest",
        "provider": [
            {
                "id": "https://example.org/about",
                "type": "Agent",
                "label": {"en": ["Example Organization"]},
                "homepage": [
                    {
                        "id": "https://example.org/",
                        "type": "Text",
                        "label": {"en": ["Example Organization Homepage"]},
                        "format": "text/html"
                    }
                ],
                "logo": [
                    {
                        "id": "https://example.org/images/logo.png",
                        "type": "Image",
                        "format": "image/png",
                        "height": 100,
                        "width": 120
                    }
                ],
                "seeAlso": [
                    {
                        "id": "https://data.example.org/about/us.jsonld",
                        "type": "Dataset",
                        "format": "application/ld+json",
                        "profile": "https://schema.org/"
                    }
                ]
            }
        ]
    }