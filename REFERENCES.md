# Reference files for all pipelines

Last modified dates on GATK bundle files are all 2013
Need to check if GATK has updated these since!

NOTE: This is not the official, final list - see iPassport for this.

## Panels
In /scratch/WRGL/bundle2.8/b37
* human_g1k_v37.fasta
 * also in Z:/WRGLPipeline/Genotype
  * This is the version used for analysis
  * includes .fasta, .fai, .dict (for GATK), and .nix (unknown use)
  * from 1000 genomes
   * ftp://ftp.1000genomes.ebi.ac.uk/vol1/ftp/technical/reference/human_g1k_v37.fasta.gz
* GRCh37.*
 * in /scratch/WRGL/bundle2.8/b37
 * BWA reference file
  * might be from ftp://ftp.ensembl.org/pub/release-75/fasta/homo_sapiens/dna/
* 1000G_phase1.indels.b37.vcf
 * also in Z:/WRGLPipeline/Genotype
 * in GATK Resource Bundle
  * https://github.com/bahlolab/bioinfotools/blob/master/GATK/resource_bundle.md
* Mills_and_1000G_gold_standard.indels.b37.vcf
 * also in Z:/WRGLPipeline/Genotype
 * in GATK Resource Bundle
 * https://github.com/bahlolab/bioinfotools/blob/master/GATK/resource_bundle.md
* dbsnp_138.b37.vcf
 * in GATK Resource Bundle
 * https://github.com/bahlolab/bioinfotools/blob/master/GATK/resource_bundle.md
* chrlengths.txt
 * columns 1 and 2 of human_g1k_v37.fasta.fai
 * not clear what it's used by - may be deletable
 
## Genotyping
In Z:/WRGLPipeline/Genotype
* human_g1k_v37.fasta
 * also in /scratch/WRGL/bundle2.8/b37
 * includes .fasta, .fai, .dict (for GATK), and .nix (unknown use)
 * from 1000 genomes
  * ftp://ftp.1000genomes.ebi.ac.uk/vol1/ftp/technical/reference/human_g1k_v37.fasta.gz
* 1000G_phase1.indels.b37.vcf
 * also in /scratch/WRGL/bundle2.8/b37
 * in GATK Resource Bundle
  * https://github.com/bahlolab/bioinfotools/blob/master/GATK/resource_bundle.md
* Mills_and_1000G_gold_standard.indels.b37.vcf
 * also in /scratch/WRGL/bundle2.8/b37
 * in GATK Resource Bundle
 * https://github.com/bahlolab/bioinfotools/blob/master/GATK/resource_bundle.md
* CosmicV70Indels.vcf
 * Cosmic is now on release V82
 * We should probably update this!
* GenomeSize.xml
 * Not sure about source or use